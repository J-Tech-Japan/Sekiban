using System.Text;
using System.Text.Json;
using System.IO.Compression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Runtime.Native;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Tags;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     Production entry-point proof for SEK-G42. This enters through NativeProjectionActorHost, which delegates to
///     NativeProjectionSnapshotHandler.RestoreSnapshotFromStreamAsync — the same state-store activation path in the
///     incident trace — rather than calling a registry helper in isolation.
/// </summary>
public sealed class StreamingSnapshotRestoreProductionPathTests
{
    [Theory]
    [InlineData(false)] // v10 envelope: raw outer JSON
    [InlineData(true)]  // v9 envelope: outer gzip JSON
    public async Task Native_restore_entry_keeps_offloaded_payload_streamed_and_blocks_query_apply_and_persist_until_success(
        bool gzipOuterEnvelope)
    {
        var sourceTypes = CreateRegistry();
        var blob = new BlockingBlobAccessor();
        var envelope = await CreateOffloadedEnvelopeAsync(sourceTypes, blob, "restored-through-native-host");
        var targetTypes = new CountingStreamingRegistry(CreateRegistry());
        var targetDomain = CreateDomain(targetTypes);
        using var services = new ServiceCollection()
            .AddSingleton<IBlobStorageSnapshotAccessor>(blob)
            .BuildServiceProvider();
        var host = new NativeProjectionActorHost(
            targetDomain,
            services,
            new NativeMultiProjectionProjectionPrimitive(targetDomain),
            StreamingRestoreProjector.MultiProjectorName,
            new GeneralMultiProjectionActorOptions { SafeWindowMs = 1000 },
            NullLogger.Instance);

        await using var envelopeStream = await SerializeEnvelopeAsync(envelope, targetDomain.JsonSerializerOptions, gzipOuterEnvelope);
        envelopeStream.Position = 0;

        // This call is the real activation-side OOM entry point. The blob stream forbids Length/Seek and releases only
        // when the test says so, which exposes any query/apply/persistence attempt that would otherwise race a restore.
        var restoreTask = host.RestoreSnapshotFromStreamAsync(envelopeStream, CancellationToken.None);
        var payloadStream = await blob.Opened.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await payloadStream.FirstReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(restoreTask.IsCompleted);
        var queryDuringRestore = await host.GetStateAsync(canGetUnsafeState: true);
        Assert.False(queryDuringRestore.IsSuccess);
        Assert.Contains("restore is still in progress", queryDuringRestore.GetException().Message, StringComparison.OrdinalIgnoreCase);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.AddSerializableEventsAsync([], finishedCatchUp: true));

        await using var persistenceTarget = new MemoryStream();
        var persistDuringRestore = await host.WriteSnapshotForPersistenceToStreamAsync(
            persistenceTarget,
            canGetUnsafeState: false,
            offloadThresholdBytes: 1,
            CancellationToken.None);
        Assert.False(persistDuringRestore.IsSuccess);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.GetProjectionHeadStatusAsync());

        // The production reflection registry owns the actual GZipStream + JsonSerializer.DeserializeAsync path; the
        // counter wraps it only at the optional capability seam. No legacy Deserialize(byte[]) call is permitted.
        Assert.Equal(1, targetTypes.StreamDeserializeCalls);
        Assert.Equal(0, targetTypes.BufferedDeserializeCalls);
        Assert.Equal(0, payloadStream.LengthAccesses);
        Assert.Equal(0, payloadStream.SeekCalls);
        Assert.False(payloadStream.IsDisposed);

        payloadStream.Release();
        var restoreResult = await restoreTask;

        Assert.True(restoreResult.IsSuccess);
        Assert.Equal(2, targetTypes.StreamDeserializeCalls);
        Assert.Equal(0, targetTypes.BufferedDeserializeCalls);
        Assert.True(payloadStream.ReadCalls > 1);
        Assert.True(payloadStream.IsDisposed);
        var state = await host.GetStateAsync(canGetUnsafeState: true);
        Assert.True(state.IsSuccess);
        Assert.Equal("restored-through-native-host", Assert.IsType<StreamingRestoreProjector>(state.GetValue().Payload).Value);
    }

    private static async Task<MemoryStream> SerializeEnvelopeAsync(
        SerializableMultiProjectionStateEnvelope envelope,
        JsonSerializerOptions options,
        bool gzipOuterEnvelope)
    {
        var stream = new MemoryStream();
        if (gzipOuterEnvelope)
        {
            await using (var gzip = new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: true))
            {
                await JsonSerializer.SerializeAsync(gzip, envelope, options);
            }
        }
        else
        {
            await JsonSerializer.SerializeAsync(stream, envelope, options);
        }

        return stream;
    }

    private static SimpleMultiProjectorTypes CreateRegistry()
    {
        var types = new SimpleMultiProjectorTypes();
        types.RegisterProjector<StreamingRestoreProjector>();
        return types;
    }

    private static DcbDomainTypes CreateDomain(ICoreMultiProjectorTypes types)
    {
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<StreamingRestoreAdded>(nameof(StreamingRestoreAdded));
        return new DcbDomainTypes(
            eventTypes,
            new SimpleTagTypes(),
            new SimpleTagProjectorTypes(),
            new SimpleTagStatePayloadTypes(),
            types,
            new SimpleQueryTypes());
    }

    private static async Task<SerializableMultiProjectionStateEnvelope> CreateOffloadedEnvelopeAsync(
        ICoreMultiProjectorTypes sourceTypes,
        BlockingBlobAccessor blob,
        string value)
    {
        var actor = new GeneralMultiProjectionActor(CreateDomain(sourceTypes), StreamingRestoreProjector.MultiProjectorName);
        await actor.AddEventsAsync([CreateEvent(value)]);
        var snapshot = await actor.BuildSnapshotEnvelopeAsync(
            canGetUnsafeState: true,
            blobAccessor: blob,
            offloadThresholdBytes: 1);
        Assert.True(snapshot.IsSuccess);
        Assert.True(snapshot.GetValue().IsOffloaded);
        return snapshot.GetValue();
    }

    private static Event CreateEvent(string value) => new(
        new StreamingRestoreAdded(value),
        SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid()),
        nameof(StreamingRestoreAdded),
        Guid.NewGuid(),
        new EventMetadata(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "streaming-restore-test"),
        []);

    public sealed record StreamingRestoreAdded(string Value) : IEventPayload;

    public sealed record StreamingRestoreProjector(string Value) : IMultiProjector<StreamingRestoreProjector>
    {
        public StreamingRestoreProjector() : this(string.Empty) { }
        public static string MultiProjectorName => "streaming-native-restore";
        public static string MultiProjectorVersion => "1";
        public static StreamingRestoreProjector GenerateInitialPayload() => new(string.Empty);

        public static ResultBox<StreamingRestoreProjector> Project(
            StreamingRestoreProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) =>
            ev.Payload is StreamingRestoreAdded added
                ? ResultBox.FromValue(new StreamingRestoreProjector(added.Value))
                : ResultBox.FromValue(payload);
    }

    private sealed class CountingStreamingRegistry(ICoreMultiProjectorTypes inner) : ICoreMultiProjectorTypes, IStreamingMultiProjectorTypes
    {
        private readonly IStreamingMultiProjectorTypes _streaming = Assert.IsAssignableFrom<IStreamingMultiProjectorTypes>(inner);
        public int StreamDeserializeCalls { get; private set; }
        public int BufferedDeserializeCalls { get; private set; }

        public ResultBox<IMultiProjectionPayload> Project(string multiProjectorName, IMultiProjectionPayload payload,
            Event ev, List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold) =>
            inner.Project(multiProjectorName, payload, ev, tags, domainTypes, safeWindowThreshold);
        public ResultBox<string> GetProjectorVersion(string multiProjectorName) => inner.GetProjectorVersion(multiProjectorName);
        public IReadOnlyList<string> GetAllProjectorNames() => inner.GetAllProjectorNames();
        public ResultBox<Func<IMultiProjectionPayload>> GetInitialPayloadGenerator(string multiProjectorName) =>
            inner.GetInitialPayloadGenerator(multiProjectorName);
        public ResultBox<Type> GetProjectorType(string multiProjectorName) => inner.GetProjectorType(multiProjectorName);
        public ResultBox<IMultiProjectionPayload> GenerateInitialPayload(string multiProjectorName) =>
            inner.GenerateInitialPayload(multiProjectorName);
        public ResultBox<IMultiProjectionPayload> Deserialize(byte[] data, string multiProjectorName, JsonSerializerOptions jsonOptions) =>
            inner.Deserialize(data, multiProjectorName, jsonOptions);
        public ResultBox<SerializationResult> Serialize(string projectorName, DcbDomainTypes domainTypes,
            string safeWindowThreshold, IMultiProjectionPayload payload) =>
            inner.Serialize(projectorName, domainTypes, safeWindowThreshold, payload);
        public ResultBox<SerializationSizeInfo> SerializeToStream(string projectorName, DcbDomainTypes domainTypes,
            string safeWindowThreshold, IMultiProjectionPayload payload, Stream destination) =>
            inner.SerializeToStream(projectorName, domainTypes, safeWindowThreshold, payload, destination);
        public ResultBox<IMultiProjectionPayload> Deserialize(string projectorName, DcbDomainTypes domainTypes,
            string safeWindowThreshold, byte[] data)
        {
            BufferedDeserializeCalls++;
            return inner.Deserialize(projectorName, domainTypes, safeWindowThreshold, data);
        }
        public ResultBox<IMultiProjectionPayload> DeserializeJson(string projectorName, string json, DcbDomainTypes domainTypes) =>
            inner.DeserializeJson(projectorName, json, domainTypes);
        public ResultBox<bool> RegisterProjectorWithCustomSerialization<T>()
            where T : ICoreMultiProjectorWithCustomSerialization<T>, new() => inner.RegisterProjectorWithCustomSerialization<T>();
        public bool SupportsStreamDeserialization(string projectorName) => _streaming.SupportsStreamDeserialization(projectorName);
        public async Task<ResultBox<IMultiProjectionPayload>> DeserializeFromStreamAsync(
            string projectorName,
            DcbDomainTypes domainTypes,
            string safeWindowThreshold,
            Stream source,
            CancellationToken cancellationToken = default)
        {
            StreamDeserializeCalls++;
            return await _streaming.DeserializeFromStreamAsync(
                projectorName,
                domainTypes,
                safeWindowThreshold,
                source,
                cancellationToken);
        }
    }

    private sealed class BlockingBlobAccessor : IBlobStorageSnapshotAccessor
    {
        private readonly Dictionary<string, byte[]> _payloads = new(StringComparer.Ordinal);
        private int _nextKey;
        public string ProviderName => "blocking-stream-test";
        public TaskCompletionSource<BlockingCappedNonSeekableStream> Opened { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<string> WriteAsync(Stream data, string projectorName, CancellationToken cancellationToken = default)
        {
            using var copy = new MemoryStream();
            await data.CopyToAsync(copy, cancellationToken);
            var key = $"{projectorName}/{++_nextKey}";
            _payloads[key] = copy.ToArray();
            return key;
        }

        public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
        {
            var stream = new BlockingCappedNonSeekableStream(_payloads[key], maxReadBytes: 97);
            Opened.TrySetResult(stream);
            return Task.FromResult<Stream>(stream);
        }
    }

    private sealed class BlockingCappedNonSeekableStream(byte[] payload, int maxReadBytes) : Stream
    {
        private readonly MemoryStream _inner = new(payload, writable: false);
        private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> FirstReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ReadCalls { get; private set; }
        public int LengthAccesses { get; private set; }
        public int SeekCalls { get; private set; }
        public bool IsDisposed { get; private set; }

        public void Release() => _release.TrySetResult(true);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length
        {
            get
            {
                LengthAccesses++;
                throw new NotSupportedException("The production offloaded restore stream has no Length.");
            }
        }
        public override long Position
        {
            get => throw new NotSupportedException("The production offloaded restore stream has no Position.");
            set => throw new NotSupportedException("The production offloaded restore stream has no Position.");
        }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            ReadCalls++;
            return _inner.Read(buffer[..Math.Min(maxReadBytes, buffer.Length)]);
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            FirstReadStarted.TrySetResult(true);
            await _release.Task.WaitAsync(cancellationToken);
            ReadCalls++;
            return await _inner.ReadAsync(buffer[..Math.Min(maxReadBytes, buffer.Length)], cancellationToken);
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            SeekCalls++;
            throw new NotSupportedException("The production offloaded restore stream cannot seek.");
        }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            if (disposing)
            {
                _inner.Dispose();
            }
            base.Dispose(disposing);
        }
        public override async ValueTask DisposeAsync()
        {
            IsDisposed = true;
            await _inner.DisposeAsync();
            await base.DisposeAsync();
        }
    }
}
