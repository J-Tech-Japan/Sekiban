using System.Buffers;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Unicode;
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
    /// single JSON token plus the insignificant whitespace immediately before it; for a property this includes
    /// the name, colon and surrounding whitespace up to the start of the value. Peak decoder buffer memory is
    /// about twice that size plus a small constant. Real Sekiban writers emit compact JSON. This bound covers
    /// only the decoder's buffer; providers may materialize the whole state data when reading a record.
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
        // Slot reads go through the sole checkpoint owner (SEK-G20); this read-only coordinator never mutates or adopts.
        var checkpoints = new CheckpointMutationCoordinator(store, static () => { });
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            CheckpointLifecycle? lifecycle = null;
            string? detail = null;
            if (checkpoints.IsCapable)
            {
                try
                {
                    var result = await checkpoints.ReadSlotAsync(entry.ProjectorName, entry.ProjectorVersion, ct).ConfigureAwait(false);
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
                if (result.IsSuccess) stream = result.GetValue();
                ct.ThrowIfCancellationRequested();
                if (!result.IsSuccess) throw result.GetException();
                if (stream is null) failure = "Open returned no stream.";
            }
            catch (Exception ex)
            {
                if (stream is not null) await stream.DisposeAsync().ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                failure = "Open: " + ex.Message;
                stream = null;
            }
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
            catch (Exception ex)
            {
                ct.ThrowIfCancellationRequested();
                failure = ex is JsonException && ex.Message == "invalid UTF-8" ? ex.Message : "Decode: " + ex.Message;
                decoded = null;
            }
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
                if (reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName &&
                    !(reader.HasValueSequence ? IsValidUtf8(reader.ValueSequence) : Utf8.IsValid(reader.ValueSpan)))
                    throw new JsonException("invalid UTF-8");
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

        private static bool IsValidUtf8(ReadOnlySequence<byte> bytes)
        {
            var reader = new SequenceReader<byte>(bytes);
            while (!reader.End)
            {
                var span = reader.UnreadSpan;
                // Validate each segment in place, leaving only a possible split scalar for the sequence reader.
                var start = span.Length - 1;
                while (start > 0 && (span[start] & 0xc0) == 0x80 && span.Length - start < 4) start--;
                var first = span[start];
                var length = first < 0x80 ? 1 : first is >= 0xc2 and <= 0xdf ? 2
                    : first is >= 0xe0 and <= 0xef ? 3 : first is >= 0xf0 and <= 0xf4 ? 4 : 0;
                if (length <= span.Length - start)
                {
                    if (!Utf8.IsValid(span)) return false;
                    reader.Advance(span.Length);
                    continue;
                }
                if (!Utf8.IsValid(span[..start])) return false;
                reader.Advance(start + 1);
                // Check continuation bytes across segment boundaries without copying them.
                for (var i = 1; i < length; i++)
                {
                    if (!reader.TryRead(out var next) || (next & 0xc0) != 0x80) return false;
                    if (i == 1 && (first == 0xe0 && next < 0xa0 || first == 0xed && next >= 0xa0 ||
                        first == 0xf0 && next < 0x90 || first == 0xf4 && next >= 0x90)) return false;
                }
            }
            return true;
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
