using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Storage.Checkpoints;

namespace Sekiban.Dcb.Snapshots;

/// <summary>Observes snapshot references in the store's current ServiceId, including old versions and tombstones.</summary>
public static class OffloadKeyEnumerator
{
    /// <summary>
    /// Enumerates row and envelope references. Absence alone never authorizes deletion; reads may be eventually
    /// consistent. Any Undecodable requires a GC to delete nothing. Pass the application's naming options.
    /// The forward-only decoder never materializes InlineState. Its buffer grows to accommodate the largest
    /// single JSON token, so peak memory is bounded by that token's size (plus a fixed initial buffer), not constant.
    /// List failures and caller cancellation propagate; other per-row failures become observations.
    /// </summary>
    public static async IAsyncEnumerable<OffloadKeyReference> EnumerateAsync(
        IMultiProjectionStateStore store,
        JsonSerializerOptions? jsonOptions = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var listed = await store.ListAllAsync(ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (!listed.IsSuccess) throw listed.GetException();
        var entries = listed.GetValue();
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            CheckpointLifecycle? lifecycle = null;
            string? detail = null;
            if (store is IGenerationAwareCheckpointStore checkpoints)
            {
                try
                {
                    var result = await checkpoints.ReadCheckpointSlotAsync(entry.ProjectorName, entry.ProjectorVersion, ct).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    if (!result.IsSuccess) throw result.GetException();
                    var slot = result.GetValue();
                    if (slot.Exists) lifecycle = slot.Lifecycle;
                    else detail = "Checkpoint slot does not exist.";
                }
                catch (Exception ex) { ct.ThrowIfCancellationRequested(); detail = "Checkpoint slot: " + ex.Message; }
            }

            OffloadKeyReference Reference(OffloadKeyReferenceKind kind, string? key = null, string? provider = null, string? error = null) =>
                new(entry.ProjectorName, entry.ProjectorVersion, kind, key, provider, lifecycle,
                    error is null ? detail : detail is null ? error : detail + " " + error);

            MultiProjectionStateRecord? record = null;
            string? failure = null;
            try
            {
                var result = await store.GetLatestForVersionAsync(entry.ProjectorName, entry.ProjectorVersion, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (!result.IsSuccess) throw result.GetException();
                var optional = result.GetValue();
                if (optional.HasValue) record = optional.GetValue();
                else failure = "Listed row no longer exists.";
            }
            catch (Exception ex) { ct.ThrowIfCancellationRequested(); failure = "Lookup: " + ex.Message; }
            if (record is null)
            {
                yield return Reference(OffloadKeyReferenceKind.Undecodable, error: failure);
                continue;
            }
            if (record.IsOffloaded)
            {
                yield return string.IsNullOrEmpty(record.OffloadKey)
                    ? Reference(OffloadKeyReferenceKind.Undecodable, error: "Row offload key is missing.")
                    : Reference(OffloadKeyReferenceKind.Row, record.OffloadKey, record.OffloadProvider);
            }

            Stream? stream = null;
            try
            {
                var result = await store.OpenStateDataReadStreamAsync(record, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (!result.IsSuccess) throw result.GetException();
                stream = result.GetValue();
                if (stream is null) failure = "Open returned no stream.";
            }
            catch (Exception ex) { ct.ThrowIfCancellationRequested(); failure = "Open: " + ex.Message; }
            if (stream is null)
            {
                yield return Reference(OffloadKeyReferenceKind.Undecodable, error: failure);
                continue;
            }

            Decoder? decoded = null;
            try
            {
                await using (stream)
                {
                    decoded = await DecodeAsync(stream, jsonOptions, ct).ConfigureAwait(false);
                }
                ct.ThrowIfCancellationRequested();
            }
            catch (Exception ex) { ct.ThrowIfCancellationRequested(); failure = "Decode: " + ex.Message; decoded = null; }
            if (decoded is null) yield return Reference(OffloadKeyReferenceKind.Undecodable, error: failure);
            else if (decoded.IsOffloaded == true) yield return Reference(OffloadKeyReferenceKind.Envelope, decoded.Key, decoded.Provider);
        }
    }

    private static async Task<Decoder> DecodeAsync(Stream source, JsonSerializerOptions? options, CancellationToken ct)
    {
        var prefix = new byte[2];
        var count = 0;
        while (count < 2)
        {
            var read = await source.ReadAsync(prefix.AsMemory(count, 2 - count), ct).ConfigureAwait(false);
            if (read == 0) break;
            count += read;
        }
        using var prefixed = new PrefixStream(prefix.AsMemory(0, count), source);
        using var gzip = count == 2 && prefix[0] == 0x1f && prefix[1] == 0x8b
            ? new GZipStream(prefixed, CompressionMode.Decompress, leaveOpen: true) : null;
        Stream input = gzip is null ? prefixed : gzip;
        var buffer = new byte[4096];
        var used = 0;
        var state = new JsonReaderState();
        var decoder = new Decoder(options);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = await input.ReadAsync(buffer.AsMemory(used), ct).ConfigureAwait(false);
            var final = read == 0;
            used += read;
            var consumed = decoder.Consume(buffer.AsSpan(0, used), final, ref state);
            buffer.AsSpan(consumed, used - consumed).CopyTo(buffer);
            used -= consumed;
            if (final) break;
            if (used == buffer.Length) Array.Resize(ref buffer, checked(buffer.Length * 2));
        }
        decoder.Validate();
        return decoder;
    }

    // Walk all containers token by token. TrySkip on an incomplete container would retain its entire subtree.
    private sealed class Decoder(JsonSerializerOptions? options)
    {
        public bool? IsOffloaded;
        public string? Key;
        public string? Provider;
        private bool _inState;
        private bool _started, _ended, _offloadedObject, _flagSeen, _stateSeen, _keySeen, _providerSeen;
        private string? _topProperty, _nestedProperty;

        private bool Matches(string name, string clr) =>
            string.Equals(name, clr, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, JsonNamingPolicy.CamelCase.ConvertName(clr), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, options?.PropertyNamingPolicy?.ConvertName(clr), StringComparison.OrdinalIgnoreCase);

        public int Consume(ReadOnlySpan<byte> bytes, bool final, ref JsonReaderState state)
        {
            var reader = new Utf8JsonReader(bytes, final, state);
            while (reader.Read())
            {
                if (!_started)
                {
                    if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Envelope must be an object.");
                    _started = true;
                    continue;
                }
                if (_ended) throw new JsonException("Trailing JSON.");
                if (reader.CurrentDepth == 0 && reader.TokenType == JsonTokenType.EndObject) { _ended = true; continue; }
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    // Unrelated nested property names need not be allocated.
                    if (reader.CurrentDepth == 1) _topProperty = reader.GetString();
                    else if (reader.CurrentDepth == 2 && _inState)
                        _nestedProperty = reader.GetString();
                    continue;
                }
                if (reader.CurrentDepth == 1 && reader.TokenType == JsonTokenType.EndObject) _inState = false;
                if (reader.CurrentDepth == 1 && _topProperty is { } top)
                {
                    if (Matches(top, "IsOffloaded"))
                    {
                        if (_flagSeen) throw new JsonException("Duplicate IsOffloaded.");
                        _flagSeen = true;
                        if (reader.TokenType is not (JsonTokenType.True or JsonTokenType.False))
                            throw new JsonException("IsOffloaded must be a boolean.");
                        IsOffloaded = reader.GetBoolean();
                    }
                    else if (Matches(top, "OffloadedState"))
                    {
                        if (_stateSeen) throw new JsonException("Duplicate OffloadedState.");
                        _stateSeen = true;
                        _offloadedObject = reader.TokenType == JsonTokenType.StartObject;
                        _inState = _offloadedObject;
                    }
                    // Clear the pending name; container contents are handled at their own depth.
                    _topProperty = null;
                }
                if (reader.CurrentDepth == 2 && _inState && _nestedProperty is { } nested)
                {
                    if (Matches(nested, "OffloadKey"))
                    {
                        if (_keySeen) throw new JsonException("Duplicate OffloadKey.");
                        _keySeen = true;
                        Key = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                    }
                    else if (Matches(nested, "StorageProvider"))
                    {
                        if (_providerSeen) throw new JsonException("Duplicate StorageProvider.");
                        _providerSeen = true;
                        Provider = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                    }
                    _nestedProperty = null;
                }
            }
            state = reader.CurrentState;
            return checked((int)reader.BytesConsumed);
        }

        public void Validate()
        {
            if (!_started || !_ended) throw new JsonException("Empty or truncated envelope.");
            if (!_flagSeen) throw new JsonException("IsOffloaded is missing.");
            if (IsOffloaded == true && (!_offloadedObject || string.IsNullOrEmpty(Key)))
                throw new JsonException("OffloadedState must be an object with a non-empty string OffloadKey.");
        }
    }

    private sealed class PrefixStream(ReadOnlyMemory<byte> prefix, Stream source) : Stream
    {
        private ReadOnlyMemory<byte> _remaining = prefix;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_remaining.IsEmpty)
            {
                var count = Math.Min(buffer.Length, _remaining.Length);
                _remaining[..count].CopyTo(buffer);
                _remaining = _remaining[count..];
                return count;
            }
            return await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
