using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Runtime.Native;
using Sekiban.Dcb.Snapshots;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

public sealed partial class StreamingSnapshotRestoreProductionPathTests
{
    [Fact]
    public async Task Native_restore_open_failure_preserves_the_original_exception_and_never_selects_buffered_fallback()
    {
        var sourceTypes = CreateRegistry();
        var blob = new BlockingBlobAccessor { BlockOnFirstRead = false };
        var envelope = await CreateOffloadedEnvelopeAsync(sourceTypes, blob, "open-failure-source");
        var expected = new IOException("injected-open-read-failure");
        blob.OpenReadFailure = expected;
        var targetTypes = new CountingStreamingRegistry(CreateRegistry());
        var targetDomain = CreateDomain(targetTypes);
        var logger = new RestoreRecordingLogger();
        using var services = new ServiceCollection().AddSingleton<IBlobStorageSnapshotAccessor>(blob).BuildServiceProvider();
        var host = CreateHost(targetDomain, services, logger);

        var result = await RestoreFromEnvelopeAsync(host, envelope, targetDomain);

        Assert.False(result.IsSuccess);
        Assert.Same(expected, result.GetException());
        Assert.Equal(1, blob.OpenReadCalls);
        Assert.Equal(0, targetTypes.StreamDeserializeCalls);
        Assert.Equal(0, targetTypes.BufferedDeserializeCalls);
        AssertNoCapabilityAbsentFallbackLog(logger);
    }

    [Fact]
    public async Task Native_restore_midstream_read_failure_preserves_the_original_exception_and_never_retries_buffered_fallback()
    {
        var sourceTypes = CreateRegistry();
        var blob = new BlockingBlobAccessor { BlockOnFirstRead = false };
        var envelope = await CreateOffloadedEnvelopeAsync(sourceTypes, blob, "read-failure-source");
        var expected = new IOException("injected-mid-stream-read-failure");
        blob.MidStreamReadFailure = expected;
        // PrefixBufferedStream consumes the gzip marker first; fail after the stream capability has actually started.
        blob.FailOnReadNumber = 3;
        var targetTypes = new CountingStreamingRegistry(CreateRegistry());
        var targetDomain = CreateDomain(targetTypes);
        var logger = new RestoreRecordingLogger();
        using var services = new ServiceCollection().AddSingleton<IBlobStorageSnapshotAccessor>(blob).BuildServiceProvider();
        var host = CreateHost(targetDomain, services, logger);

        var result = await RestoreFromEnvelopeAsync(host, envelope, targetDomain);
        var stream = await blob.Opened.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result.IsSuccess);
        Assert.Same(expected, result.GetException());
        Assert.Equal(1, blob.OpenReadCalls);
        Assert.Equal(1, targetTypes.StreamDeserializeCalls);
        Assert.Equal(0, targetTypes.BufferedDeserializeCalls);
        Assert.True(stream.ReadCalls >= blob.FailOnReadNumber);
        AssertNoCapabilityAbsentFallbackLog(logger);
    }

    [Fact]
    public async Task Terminal_native_restore_failure_quarantines_query_catchup_apply_promotion_and_persistence_until_a_successful_recovery()
    {
        var sourceTypes = CreateRegistry();
        var blob = new BlockingBlobAccessor { BlockOnFirstRead = false };
        var envelope = await CreateOffloadedEnvelopeAsync(sourceTypes, blob, "recovered-snapshot");
        var expected = new IOException("terminal-stream-read-failure");
        blob.MidStreamReadFailure = expected;
        blob.FailOnReadNumber = 3;
        var targetTypes = new CountingStreamingRegistry(CreateRegistry());
        var targetDomain = CreateDomain(targetTypes);
        var logger = new RestoreRecordingLogger();
        using var services = new ServiceCollection().AddSingleton<IBlobStorageSnapshotAccessor>(blob).BuildServiceProvider();
        var host = CreateHost(targetDomain, services, logger);

        await host.AddSerializableEventsAsync([CreateSerializableEvent("old-state")], finishedCatchUp: true);
        var before = await host.GetStateAsync(canGetUnsafeState: true);
        Assert.True(before.IsSuccess);
        var rollbackBefore = CaptureHostRestoreRollbackSnapshot(host);

        var restoreFailure = await RestoreFromEnvelopeAsync(host, envelope, targetDomain);
        Assert.False(restoreFailure.IsSuccess);
        Assert.Same(expected, restoreFailure.GetException());
        Assert.Equal(1, targetTypes.StreamDeserializeCalls);
        Assert.Equal(0, targetTypes.BufferedDeserializeCalls);
        AssertNoCapabilityAbsentFallbackLog(logger);

        // The production host's ordinary query and metadata query cannot expose the state that existed before failure.
        var query = await host.GetStateAsync(canGetUnsafeState: true);
        Assert.False(query.IsSuccess);
        Assert.Same(expected, query.GetException());
        var metadata = await host.GetStateMetadataAsync(includeUnsafe: true);
        Assert.False(metadata.IsSuccess);
        Assert.Same(expected, metadata.GetException());
        Assert.False(await host.IsSortableUniqueIdReceivedAsync(before.GetValue().LastSortableUniqueId));

        // Catch-up and normal apply are separate public invocations. Neither may mutate the quarantined actor.
        var catchUpFailure = await Assert.ThrowsAsync<IOException>(
            () => host.AddSerializableEventsAsync([], finishedCatchUp: true));
        Assert.Same(expected, catchUpFailure);
        var applyFailure = await Assert.ThrowsAsync<IOException>(
            () => host.AddSerializableEventsAsync([CreateSerializableEvent("must-not-apply")], finishedCatchUp: false));
        Assert.Same(expected, applyFailure);
        var headFailure = await Assert.ThrowsAsync<IOException>(() => host.GetProjectionHeadStatusAsync());
        Assert.Same(expected, headFailure);
        var promotionFailure = Assert.Throws<IOException>(() => host.ForcePromoteBufferedEvents());
        Assert.Same(expected, promotionFailure);
        var compactionFailure = Assert.Throws<IOException>(() => host.CompactSafeHistory());
        Assert.Same(expected, compactionFailure);

        await using (var persistenceTarget = new MemoryStream())
        {
            var persistence = await host.WriteSnapshotForPersistenceToStreamAsync(
                persistenceTarget,
                canGetUnsafeState: false,
                offloadThresholdBytes: 1,
                CancellationToken.None);
            Assert.False(persistence.IsSuccess);
            Assert.Same(expected, persistence.GetException());
            Assert.Equal(0, persistenceTarget.Length);
        }

        await using (var snapshotTarget = new MemoryStream())
        {
            var snapshot = await host.WriteSnapshotToStreamAsync(snapshotTarget, canGetUnsafeState: true, CancellationToken.None);
            Assert.False(snapshot.IsSuccess);
            Assert.Same(expected, snapshot.GetException());
            Assert.Equal(0, snapshotTarget.Length);
        }

        AssertHostRestoreRollbackSnapshotUnchanged(host, rollbackBefore);

        // Recovery itself remains allowed. A complete later restore atomically replaces state and clears the quarantine.
        blob.MidStreamReadFailure = null;
        blob.FailOnReadNumber = int.MaxValue;
        var recovered = await RestoreFromEnvelopeAsync(host, envelope, targetDomain);
        Assert.True(recovered.IsSuccess);
        var recoveredState = await host.GetStateAsync(canGetUnsafeState: true);
        Assert.True(recoveredState.IsSuccess);
        Assert.Equal("recovered-snapshot", Assert.IsType<StreamingRestoreProjector>(recoveredState.GetValue().Payload).Value);

        await host.AddSerializableEventsAsync([CreateSerializableEvent("after-recovery")], finishedCatchUp: true);
        var afterApply = await host.GetStateAsync(canGetUnsafeState: true);
        Assert.True(afterApply.IsSuccess);
        Assert.Equal("after-recovery", Assert.IsType<StreamingRestoreProjector>(afterApply.GetValue().Payload).Value);

        await using var recoveredPersistenceTarget = new MemoryStream();
        var recoveredPersistence = await host.WriteSnapshotForPersistenceToStreamAsync(
            recoveredPersistenceTarget,
            canGetUnsafeState: false,
            offloadThresholdBytes: 1,
            CancellationToken.None);
        Assert.True(recoveredPersistence.IsSuccess);
        Assert.True(recoveredPersistenceTarget.Length > 0);
    }

    private static NativeProjectionActorHost CreateHost(
        DcbDomainTypes domain,
        IServiceProvider services,
        ILogger logger) => new(
        domain,
        services,
        new NativeMultiProjectionProjectionPrimitive(domain),
        StreamingRestoreProjector.MultiProjectorName,
        new GeneralMultiProjectionActorOptions { SafeWindowMs = 1000 },
        logger);

    private static async Task<ResultBox<bool>> RestoreFromEnvelopeAsync(
        NativeProjectionActorHost host,
        SerializableMultiProjectionStateEnvelope envelope,
        DcbDomainTypes domain)
    {
        await using var stream = await SerializeEnvelopeAsync(envelope, domain.JsonSerializerOptions, gzipOuterEnvelope: false);
        stream.Position = 0;
        return await host.RestoreSnapshotFromStreamAsync(stream, CancellationToken.None);
    }

    private static SerializableEvent CreateSerializableEvent(string value) => new(
        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new StreamingRestoreAdded(value)),
        SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid()),
        Guid.NewGuid(),
        new EventMetadata(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "streaming-restore-test"),
        [],
        nameof(StreamingRestoreAdded));

    private static void AssertNoCapabilityAbsentFallbackLog(RestoreRecordingLogger logger) =>
        Assert.DoesNotContain(
            logger.Messages,
            message => message.Contains("capability-absent", StringComparison.Ordinal));

    private static HostRestoreRollbackSnapshot CaptureHostRestoreRollbackSnapshot(NativeProjectionActorHost host)
    {
        var actor = GetHostActor(host);
        return new HostRestoreRollbackSnapshot(
            ReadActorField<object>(actor, "_singleStateAccessor"),
            ReadActorField<Guid>(actor, "_unsafeLastEventId"),
            ReadActorField<string>(actor, "_unsafeLastSortableUniqueId"),
            ReadActorField<int>(actor, "_unsafeVersion"),
            ReadActorField<bool>(actor, "_isCatchedUp"));
    }

    private static void AssertHostRestoreRollbackSnapshotUnchanged(
        NativeProjectionActorHost host,
        HostRestoreRollbackSnapshot before)
    {
        var actor = GetHostActor(host);
        Assert.Same(before.StateAccessor, ReadActorField<object>(actor, "_singleStateAccessor"));
        Assert.Equal(before.LastEventId, ReadActorField<Guid>(actor, "_unsafeLastEventId"));
        Assert.Equal(before.LastSortableUniqueId, ReadActorField<string>(actor, "_unsafeLastSortableUniqueId"));
        Assert.Equal(before.Version, ReadActorField<int>(actor, "_unsafeVersion"));
        Assert.Equal(before.IsCatchedUp, ReadActorField<bool>(actor, "_isCatchedUp"));
    }

    private static GeneralMultiProjectionActor GetHostActor(NativeProjectionActorHost host) =>
        (GeneralMultiProjectionActor)(typeof(NativeProjectionActorHost)
            .GetField("_actor", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(host)
            ?? throw new Xunit.Sdk.XunitException("NativeProjectionActorHost._actor was not found."));

    private static T ReadActorField<T>(GeneralMultiProjectionActor actor, string name) =>
        (T)(typeof(GeneralMultiProjectionActor)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(actor)
            ?? throw new Xunit.Sdk.XunitException($"Actor field '{name}' was not found."));

    private sealed record HostRestoreRollbackSnapshot(
        object StateAccessor,
        Guid LastEventId,
        string LastSortableUniqueId,
        int Version,
        bool IsCatchedUp);

    private sealed class RestoreRecordingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
