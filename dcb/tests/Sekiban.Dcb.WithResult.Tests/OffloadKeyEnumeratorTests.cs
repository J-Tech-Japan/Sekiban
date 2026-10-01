using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Storage.Checkpoints;
using Sekiban.Dcb.Testing;
using Store = Sekiban.Dcb.Testing.InMemoryMultiProjectionStateStore;
using Blob = Sekiban.Dcb.Testing.InMemoryBlobStorageSnapshotAccessor;

namespace Sekiban.Dcb.WithResult.Tests;

public class OffloadKeyEnumeratorTests
{
    private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static byte[] Envelope(bool offloaded = true, JsonSerializerOptions? options = null) =>
        JsonSerializer.SerializeToUtf8Bytes(new SerializableMultiProjectionStateEnvelope(offloaded, null,
            offloaded ? new SerializableMultiProjectionStateOffloaded("envelope-key", "memory", "T", "p", "1",
                "s", Guid.Empty, 1, true, true, 12) : null), options ?? Camel);
    private static MultiProjectionStateWriteRequest Request(string version = "1") =>
        new("p", version, "T", "s", 1, false, null, null, 1, 1, "w",
            DateTime.UtcNow, DateTime.UtcNow, "test", null);
    private static async Task<Store> Saved(byte[] bytes, string version = "1", Store? store = null)
    {
        store ??= new Store();
        using var stream = new MemoryStream(bytes);
        Assert.True((await store.UpsertFromStreamAsync(Request(version), stream, int.MaxValue)).GetValue());
        return store;
    }
    private static async Task<List<OffloadKeyReference>> Scan(IMultiProjectionStateStore store,
        JsonSerializerOptions? options = null, CancellationToken ct = default)
    {
        var results = new List<OffloadKeyReference>();
        await foreach (var reference in OffloadKeyEnumerator.EnumerateAsync(store, options, ct)) results.Add(reference);
        return results;
    }
    private static byte[] Gzip(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress, true)) gzip.Write(bytes);
        return output.ToArray();
    }

    [Fact]
    public async Task Inline_has_no_reference() => Assert.Empty(await Scan(await Saved(Envelope(false))));

    [Fact]
    public async Task Real_envelope_reports_both_versions_and_tombstones()
    {
        var store = await Saved(Envelope());
        await Saved(Envelope(), "2", store);
        var slot = (await store.ReadCheckpointSlotAsync("p", "1")).GetValue();
        await store.InvalidateWithTombstoneAsync("p", "1", CheckpointExpectation.FromSlot(slot));
        var refs = await Scan(store);
        Assert.Equal(2, refs.Count);
        Assert.All(refs, r => { Assert.Equal("envelope-key", r.OffloadKey); Assert.Equal("memory", r.StorageProvider); });
        Assert.Equal(CheckpointLifecycle.Tombstoned, refs.Single(r => r.ProjectorVersion == "1").Lifecycle);
        Assert.Equal(CheckpointLifecycle.Active, refs.Single(r => r.ProjectorVersion == "2").Lifecycle);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Short_reads_split_header_and_tokens(bool gzip)
    {
        var bytes = Envelope();
        if (gzip) bytes = Gzip(bytes);
        var store = await Saved(bytes);
        var refs = await Scan(new Wrapper(store) { ShortReads = true });
        Assert.Equal("envelope-key", Assert.Single(refs).OffloadKey);
    }

    [Fact]
    public async Task Reversed_property_order()
    {
        using var document = JsonDocument.Parse(Envelope());
        var properties = document.RootElement.EnumerateObject().Reverse();
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            foreach (var property in properties) property.WriteTo(writer);
            writer.WriteEndObject();
        }
        Assert.Equal("envelope-key", Assert.Single(await Scan(await Saved(output.ToArray()))).OffloadKey);
    }

    [Fact]
    public async Task Snake_case_requires_options()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var store = await Saved(Envelope(options: options));
        Assert.Equal(OffloadKeyReferenceKind.Undecodable, Assert.Single(await Scan(store)).Kind);
        Assert.Equal("envelope-key", Assert.Single(await Scan(store, options)).OffloadKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("garbage")]
    [InlineData("{\"isOffloaded\":true")]
    [InlineData("{}")]
    [InlineData("{\"isOffloaded\":0}")]
    [InlineData("{\"isOffloaded\":true}")]
    [InlineData("{\"isOffloaded\":true,\"offloadedState\":[]}")]
    [InlineData("{\"isOffloaded\":true,\"offloadedState\":\"bad\"}")]
    [InlineData("{\"isOffloaded\":true,\"offloadedState\":{}}")]
    [InlineData("{\"isOffloaded\":true,\"offloadedState\":{\"offloadKey\":null}}")]
    [InlineData("{\"isOffloaded\":true,\"offloadedState\":{\"offloadKey\":\"\"}}")]
    [InlineData("{\"isOffloaded\":true,\"offloadedState\":{\"offloadKey\":42}}")]
    [InlineData("{\"IsOffloaded\":true,\"isOffloaded\":false}")]
    [InlineData("{\"isOffloaded\":false} trailing")]
    public async Task Invalid_data_is_never_silent(string json)
    {
        var reference = Assert.Single(await Scan(await Saved(Encoding.UTF8.GetBytes(json))));
        Assert.Equal(OffloadKeyReferenceKind.Undecodable, reference.Kind);
        Assert.Null(reference.OffloadKey);
        Assert.False(string.IsNullOrEmpty(reference.Detail));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Multi_megabyte_inline_refills_without_materializing(bool manyTokens)
    {
        var payload = manyTokens ? "[" + string.Join(",", Enumerable.Repeat("{\"x\":1}", 400_000)) + "]"
            : "\"" + new string('x', 3_000_000) + "\"";
        var json = "{\"inlineState\":" + payload +
            ",\"offloadedState\":{\"offloadKey\":\"large-key\"},\"isOffloaded\":true}";
        Assert.Equal("large-key", Assert.Single(await Scan(await Saved(Encoding.UTF8.GetBytes(json)))).OffloadKey);
    }

    [Theory]
    [InlineData("lookup-error")]
    [InlineData("lookup-throw")]
    [InlineData("lookup-missing")]
    [InlineData("open-error")]
    [InlineData("open-throw")]
    public async Task Per_row_failures_become_undecodable(string fault)
    {
        Assert.Equal(OffloadKeyReferenceKind.Undecodable,
            Assert.Single(await Scan(new Wrapper(await Saved(Envelope())) { Fault = fault })).Kind);
    }

    [Theory]
    [InlineData("slot-error")]
    [InlineData("slot-throw")]
    [InlineData("slot-missing")]
    public async Task Slot_failures_preserve_keys(string fault)
    {
        var reference = Assert.Single(await Scan(new Wrapper(await Saved(Envelope())) { Fault = fault }));
        Assert.Equal("envelope-key", reference.OffloadKey);
        Assert.Null(reference.Lifecycle);
        Assert.NotNull(reference.Detail);
    }

    [Fact]
    public async Task List_failure_propagates() =>
        await Assert.ThrowsAsync<IOException>(async () => await Scan(new Wrapper(await Saved(Envelope())) { Fault = "list-error" }));

    [Theory]
    [InlineData("lookup-cancel")]
    [InlineData("open-cancel")]
    [InlineData("slot-cancel")]
    public async Task Wrapped_caller_cancellation_propagates(string fault)
    {
        using var cancellation = new CancellationTokenSource();
        var store = new Wrapper(await Saved(Envelope())) { Fault = fault, Cancellation = cancellation };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Scan(store, ct: cancellation.Token));
    }

    [Fact]
    public async Task Already_cancelled_caller_propagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Scan(new Store(), ct: cancellation.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Row_blob_yields_row_then_envelope_or_undecodable(bool missing)
    {
        var blobs = new Blob();
        using var data = new MemoryStream(Envelope());
        var key = await blobs.WriteAsync(data, "p");
        var wrapper = new Wrapper(await Saved(Envelope())) { Blobs = blobs, RowKey = missing ? "missing" : key };
        var refs = await Scan(wrapper);
        Assert.Equal(2, refs.Count);
        Assert.Equal(OffloadKeyReferenceKind.Row, refs[0].Kind);
        Assert.Equal(wrapper.RowKey, refs[0].OffloadKey);
        Assert.Equal(blobs.ProviderName, refs[0].StorageProvider);
        Assert.Equal(missing ? OffloadKeyReferenceKind.Undecodable : OffloadKeyReferenceKind.Envelope, refs[1].Kind);
    }

    // Reimplement only the inherited non-virtual store members that this decorator varies.
    private sealed class Wrapper(Store inner) : DelegatingCheckpointStore(inner), IMultiProjectionStateStore
    {
        public string? Fault;
        public bool ShortReads;
        public Blob? Blobs;
        public string? RowKey;
        public CancellationTokenSource? Cancellation;
        private Exception Error(string step)
        {
            if (Fault == step + "-cancel") { Cancellation!.Cancel(); return new OperationCanceledException(Cancellation.Token); }
            return new IOException(step + " failed");
        }
        Task<ResultBox<IReadOnlyList<ProjectorStateInfo>>> IMultiProjectionStateStore.ListAllAsync(CancellationToken ct) =>
            Fault == "list-error" ? Task.FromResult(ResultBox.Error<IReadOnlyList<ProjectorStateInfo>>(new IOException("list failed")))
                : Inner.ListAllAsync(ct);
        async Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> IMultiProjectionStateStore.GetLatestForVersionAsync(string p, string v, CancellationToken ct)
        {
            if (Fault == "lookup-throw") throw Error("lookup");
            if (Fault is "lookup-error" or "lookup-cancel") return ResultBox.Error<OptionalValue<MultiProjectionStateRecord>>(Error("lookup"));
            if (Fault == "lookup-missing") return ResultBox.FromValue(OptionalValue<MultiProjectionStateRecord>.Empty);
            var result = await Inner.GetLatestForVersionAsync(p, v, ct);
            if (RowKey is null) return result;
            var record = result.GetValue().GetValue() with { IsOffloaded = true, OffloadKey = RowKey, OffloadProvider = Blobs!.ProviderName };
            return ResultBox.FromValue(OptionalValue.FromValue(record));
        }
        async Task<ResultBox<Stream>> IMultiProjectionStateStore.OpenStateDataReadStreamAsync(MultiProjectionStateRecord r, CancellationToken ct)
        {
            if (Fault == "open-throw") throw Error("open");
            if (Fault is "open-error" or "open-cancel") return ResultBox.Error<Stream>(Error("open"));
            var stream = RowKey is null ? (await Inner.OpenStateDataReadStreamAsync(r, ct)).GetValue()
                : await Blobs!.OpenReadAsync(r.OffloadKey!, ct);
            return ResultBox.FromValue<Stream>(ShortReads ? new ShortReadStream(stream) : stream);
        }
        public override Task<ResultBox<CheckpointSlot>> ReadCheckpointSlotAsync(string p, string v, CancellationToken ct = default)
        {
            if (Fault == "slot-throw") throw Error("slot");
            if (Fault is "slot-error" or "slot-cancel") return Task.FromResult(ResultBox.Error<CheckpointSlot>(Error("slot")));
            if (Fault == "slot-missing") return Task.FromResult(ResultBox.FromValue(CheckpointSlot.Absent));
            return base.ReadCheckpointSlotAsync(p, v, ct);
        }
    }

    private sealed class ShortReadStream(Stream source) : Stream
    {
        private int _read;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            source.ReadAsync(buffer[..Math.Min(buffer.Length, 1 + _read++ % 7)], ct);
        public override int Read(byte[] buffer, int offset, int count) => source.Read(buffer, offset, Math.Min(count, 1 + _read++ % 7));
        protected override void Dispose(bool disposing) { if (disposing) source.Dispose(); base.Dispose(disposing); }
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
