using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     SEK-G42 production restore seam tests. These use an actual offloaded envelope, a non-seekable partial-read stream,
///     and the actor's resolver-produced input rather than exercising a registry helper in isolation.
/// </summary>
public sealed partial class StreamingSnapshotRestoreTests
{
    [Fact]
    public async Task Offloaded_gzip_json_restore_uses_stream_capability_without_a_whole_payload_byte_array()
    {
        var sourceTypes = CreateReflectionRegistry();
        var (envelope, blob) = await CreateOffloadedEnvelopeAsync(sourceTypes, ["one", "two", "three"]);
        Assert.True(blob.LastWrittenPayload is { Length: >= 2 } &&
            blob.LastWrittenPayload[0] == 0x1f && blob.LastWrittenPayload[1] == 0x8b);
        var targetTypes = new CountingStreamingRegistry(CreateReflectionRegistry());
        var target = CreateActor(targetTypes);

        var resolved = await SnapshotEnvelopeResolver.ResolveForRestoreAsync(envelope, blob);
        var stream = Assert.IsType<CappedNonSeekableStream>(resolved.PayloadStream);
        try
        {
            // The resolver propagates metadata plus the opened stream; it has not converted the offloaded bytes back
            // into any v9/v10 inline representation before the actor reaches the streaming capability.
            Assert.Null(resolved.State.PayloadJson);
            Assert.Null(resolved.State.PayloadBase64);
            Assert.Null(resolved.State.RuntimePayloadBytes);
            await target.SetResolvedSnapshotAsync(resolved);

            Assert.Equal(2, targetTypes.StreamDeserializeCalls);
            Assert.Equal(0, targetTypes.BufferedDeserializeCalls);
            Assert.Equal(0, GetLastStreamingRestoreWholePayloadAggregationCount(target));
            Assert.True(stream.ReadCalls > 1);
            Assert.Equal(0, stream.LengthAccesses);
            Assert.Equal(0, stream.SeekCalls);
            Assert.False(stream.IsDisposed);

            var state = await target.GetStateAsync();
            Assert.True(state.IsSuccess);
            Assert.Equal(["one", "two", "three"], Payload(state.GetValue()));
        }
        finally
        {
            await resolved.DisposeAsync();
        }

        Assert.True(stream.IsDisposed);
    }

    [Fact]
    public async Task Capability_absent_uses_one_observable_buffered_fallback_without_payload_logging()
    {
        const string secret = "payload-must-not-appear-in-log";
        var sourceTypes = CreateReflectionRegistry();
        var (envelope, blob) = await CreateOffloadedEnvelopeAsync(sourceTypes, [secret]);
        var targetTypes = new CountingBufferedRegistry(CreateReflectionRegistry());
        var logger = new RecordingLogger();
        var target = CreateActor(targetTypes, logger);

        await using var resolved = await SnapshotEnvelopeResolver.ResolveForRestoreAsync(envelope, blob);
        await target.SetResolvedSnapshotAsync(resolved);

        // The one fallback read is followed by the pre-existing dual-state clone, which has its own byte-array
        // serializer round-trip. The fallback log proves the offloaded input itself was buffered exactly once.
        Assert.Equal(2, targetTypes.BufferedDeserializeCalls);
        var fallback = Assert.Single(
            logger.Messages,
            message => message.Contains("capability-absent", StringComparison.Ordinal));
        Assert.Contains(StreamPayloadProjector.MultiProjectorName, fallback, StringComparison.Ordinal);
        Assert.Contains(targetTypes.GetType().FullName!, fallback, StringComparison.Ordinal);
        Assert.Contains("offloaded", fallback, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, fallback, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Intentionally_buffered_stream_registry_fails_the_same_structural_discriminant()
    {
        // A registry that merely performs chunked reads is not sufficient evidence: it can still aggregate them in a
        // MemoryStream. This negative control does exactly that through the only whole-buffer helper and must fail the
        // same seam discriminant that the production JSON+gzip registry passes above.
        var sourceTypes = CreateReflectionRegistry();
        var (envelope, blob) = await CreateOffloadedEnvelopeAsync(sourceTypes, ["buffered-negative-control"]);
        var targetTypes = new IntentionallyBufferedStreamingRegistry(CreateReflectionRegistry());
        var target = CreateActor(targetTypes);

        await using var resolved = await SnapshotEnvelopeResolver.ResolveForRestoreAsync(envelope, blob);
        await target.SetResolvedSnapshotAsync(resolved);

        Assert.Equal(2, targetTypes.StreamDeserializeCalls);
        Assert.Equal(2, targetTypes.WholePayloadAggregations);
        // The actor's production-scope counter observes StreamReadHelper itself. This is deliberately independent of
        // the control registry's own counter, so a MemoryStream/ToArray mutation at the shared seam is also killed.
        Assert.Equal(2, GetLastStreamingRestoreWholePayloadAggregationCount(target));
    }

    [Fact]
    public async Task Present_capability_failure_propagates_and_never_retries_the_buffered_path()
    {
        var sourceTypes = CreateReflectionRegistry();
        var (envelope, blob) = await CreateOffloadedEnvelopeAsync(sourceTypes, ["will-fail"]);
        var targetTypes = new FailingStreamingRegistry(CreateReflectionRegistry());
        var target = CreateActor(targetTypes);

        await using var resolved = await SnapshotEnvelopeResolver.ResolveForRestoreAsync(envelope, blob);
        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => target.SetResolvedSnapshotAsync(resolved));

        Assert.Equal("stream-capability-failure", exception.Message);
        Assert.Equal(1, targetTypes.StreamDeserializeCalls);
        Assert.Equal(0, targetTypes.BufferedDeserializeCalls);
    }

    [Fact]
    public async Task Nonseekable_partial_stream_honors_cancellation_and_caller_ownership()
    {
        var sourceTypes = CreateReflectionRegistry();
        var (envelope, blob) = await CreateOffloadedEnvelopeAsync(sourceTypes, ["cancel"]);
        var target = CreateActor(new CountingStreamingRegistry(CreateReflectionRegistry()));
        await using var resolved = await SnapshotEnvelopeResolver.ResolveForRestoreAsync(envelope, blob);
        var stream = Assert.IsType<CappedNonSeekableStream>(resolved.PayloadStream);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => target.SetResolvedSnapshotAsync(resolved, cancellation.Token));
        Assert.False(stream.IsDisposed);
    }

    [Fact]
    public async Task Stream_restore_reads_from_a_nonseekable_streams_current_position_without_disposing_it()
    {
        var sourceTypes = CreateReflectionRegistry();
        var (envelope, blob) = await CreateOffloadedEnvelopeAsync(sourceTypes, ["current-position"]);
        blob.OpenedPayloadPrefix = [0x99, 0x88, 0x77];
        var target = CreateActor(new CountingStreamingRegistry(CreateReflectionRegistry()));

        await using var resolved = await SnapshotEnvelopeResolver.ResolveForRestoreAsync(envelope, blob);
        var stream = Assert.IsType<CappedNonSeekableStream>(resolved.PayloadStream);
        await target.SetResolvedSnapshotAsync(resolved);

        var state = await target.GetStateAsync();
        Assert.True(state.IsSuccess);
        Assert.Equal(["current-position"], Payload(state.GetValue()));
        Assert.True(stream.ReadCalls > 1);
        Assert.Equal(0, stream.LengthAccesses);
        Assert.Equal(0, stream.SeekCalls);
        Assert.False(stream.IsDisposed);
    }

    [Fact]
    public async Task Truncated_gzip_payload_keeps_previous_payload_and_tracking_metadata_unchanged()
    {
        var sourceTypes = CreateReflectionRegistry();
        var (envelope, blob) = await CreateOffloadedEnvelopeAsync(sourceTypes, ["restored"]);
        blob.TruncateOpenedPayloadTo = 3;
        var target = CreateActor(new CountingStreamingRegistry(CreateReflectionRegistry()));
        await target.AddEventsAsync([CreateEvent("before")]);
        var before = await target.GetStateAsync();
        Assert.True(before.IsSuccess);
        var beforeState = before.GetValue();
        var beforeRawState = CaptureRestoreRollbackSnapshot(target);

        await using var resolved = await SnapshotEnvelopeResolver.ResolveForRestoreAsync(envelope, blob);
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => target.SetResolvedSnapshotAsync(resolved));

        var after = await target.GetStateAsync();
        Assert.False(after.IsSuccess);
        Assert.Same(exception, after.GetException());
        AssertRestoreRollbackSnapshotUnchanged(target, beforeRawState);
        Assert.Equal(["before"], Payload(beforeState));
    }

    [Fact]
    public async Task Corrupt_gzip_payload_propagates_without_a_buffered_retry_or_partial_publication()
    {
        var sourceTypes = CreateReflectionRegistry();
        var (envelope, blob) = await CreateOffloadedEnvelopeAsync(sourceTypes, ["restored"]);
        blob.OpenedPayloadOverride = [0x1f, 0x8b, 0xff, 0x00, 0x01];
        var targetTypes = new CountingStreamingRegistry(CreateReflectionRegistry());
        var target = CreateActor(targetTypes);
        await target.AddEventsAsync([CreateEvent("before-corrupt")]);
        var before = (await target.GetStateAsync()).GetValue();
        var beforeRawState = CaptureRestoreRollbackSnapshot(target);

        await using var resolved = await SnapshotEnvelopeResolver.ResolveForRestoreAsync(envelope, blob);
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => target.SetResolvedSnapshotAsync(resolved));

        Assert.Equal(1, targetTypes.StreamDeserializeCalls);
        Assert.Equal(0, targetTypes.BufferedDeserializeCalls);
        var after = await target.GetStateAsync();
        Assert.False(after.IsSuccess);
        Assert.Same(exception, after.GetException());
        AssertRestoreRollbackSnapshotUnchanged(target, beforeRawState);
        Assert.Equal(["before-corrupt"], Payload(before));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Inline_v9_and_v10_legacy_payloads_remain_equivalent(bool useV10Base64)
    {
        var registry = CreateReflectionRegistry();
        var domain = CreateDomain(registry);
        var payload = new StreamPayloadProjector(["legacy"]);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, domain.JsonSerializerOptions);
        var state = new SerializableMultiProjectionState(
            payloadJson: useV10Base64 ? null : Encoding.UTF8.GetString(bytes),
            payloadBase64: useV10Base64 ? Convert.ToBase64String(bytes) : null,
            typeof(StreamPayloadProjector).FullName!,
            StreamPayloadProjector.MultiProjectorName,
            StreamPayloadProjector.MultiProjectorVersion,
            SortableUniqueId.MinValue.Value,
            Guid.Empty,
            version: 7);
        var actor = CreateActor(registry);

        await actor.SetSnapshotAsync(new SerializableMultiProjectionStateEnvelope(false, state, null));

        var restored = await actor.GetStateAsync();
        Assert.True(restored.IsSuccess);
        Assert.Equal(["legacy"], Payload(restored.GetValue()));
        Assert.Equal(7, restored.GetValue().Version);
    }

    [Fact]
    public async Task Offloaded_gzip_aot_json_typeinfo_restores_through_the_stream_capability()
    {
        var aotTypes = new AotMultiProjectorTypes();
        aotTypes.RegisterProjector<StreamPayloadProjector>(StreamingRestoreJsonContext.Default.StreamPayloadProjector);
        var (envelope, blob) = await CreateOffloadedEnvelopeAsync(aotTypes, ["aot"]);
        Assert.True(blob.LastWrittenPayload is { Length: >= 2 } &&
            blob.LastWrittenPayload[0] == 0x1f && blob.LastWrittenPayload[1] == 0x8b);
        var target = CreateActor(aotTypes);

        await using var resolved = await SnapshotEnvelopeResolver.ResolveForRestoreAsync(envelope, blob);
        await target.SetResolvedSnapshotAsync(resolved);
        var restored = await target.GetStateAsync();

        Assert.True(restored.IsSuccess);
        Assert.Equal(["aot"], Payload(restored.GetValue()));
        Assert.IsAssignableFrom<IStreamingMultiProjectorTypes>(aotTypes);
    }

    [Fact]
    public async Task Offloaded_legacy_raw_json_reflection_payload_restores_through_the_same_stream_capability()
    {
        // Existing stores can contain uncompressed JSON payloads. The production helper checks only a two-byte prefix,
        // replays it on the same non-seekable stream, and invokes JsonSerializer.DeserializeAsync without a byte[] copy.
        var sourceTypes = CreateReflectionRegistry();
        var domain = CreateDomain(sourceTypes);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            new StreamPayloadProjector(["legacy-raw"]),
            domain.JsonSerializerOptions);
        var blob = new CappedBlobAccessor();
        var key = await blob.WriteAsync(new MemoryStream(bytes), StreamPayloadProjector.MultiProjectorName);
        Assert.False(blob.LastWrittenPayload is { Length: >= 2 } &&
            blob.LastWrittenPayload[0] == 0x1f && blob.LastWrittenPayload[1] == 0x8b);
        var envelope = new SerializableMultiProjectionStateEnvelope(
            IsOffloaded: true,
            InlineState: null,
            OffloadedState: new SerializableMultiProjectionStateOffloaded(
                key,
                blob.ProviderName,
                typeof(StreamPayloadProjector).FullName!,
                StreamPayloadProjector.MultiProjectorName,
                StreamPayloadProjector.MultiProjectorVersion,
                SortableUniqueId.MinValue.Value,
                Guid.Empty,
                Version: 2,
                IsCatchedUp: true,
                IsSafeState: true,
                PayloadLength: bytes.Length,
                OriginalSizeBytes: bytes.Length,
                CompressedSizeBytes: bytes.Length));
        var targetTypes = new CountingStreamingRegistry(CreateReflectionRegistry());
        var target = CreateActor(targetTypes);

        await using var resolved = await SnapshotEnvelopeResolver.ResolveForRestoreAsync(envelope, blob);
        await target.SetResolvedSnapshotAsync(resolved);

        var restored = await target.GetStateAsync();
        Assert.True(restored.IsSuccess);
        Assert.Equal(["legacy-raw"], Payload(restored.GetValue()));
        Assert.Equal(2, targetTypes.StreamDeserializeCalls);
        Assert.Equal(0, targetTypes.BufferedDeserializeCalls);
    }

    [Fact]
    public async Task Custom_projector_stream_capability_is_detected_per_projector()
    {
        var types = new SimpleMultiProjectorTypes();
        Assert.True(types.RegisterProjectorWithCustomSerialization<CustomStreamPayloadProjector>().IsSuccess);
        Assert.True(((IStreamingMultiProjectorTypes)types)
            .SupportsStreamDeserialization(CustomStreamPayloadProjector.MultiProjectorName));
        var (envelope, blob) = await CreateOffloadedEnvelopeAsync(
            types,
            ["custom"],
            CustomStreamPayloadProjector.MultiProjectorName);
        Assert.False(blob.LastWrittenPayload is { Length: >= 2 } &&
            blob.LastWrittenPayload[0] == 0x1f && blob.LastWrittenPayload[1] == 0x8b);
        var target = new GeneralMultiProjectionActor(
            CreateDomain(types),
            CustomStreamPayloadProjector.MultiProjectorName);

        await using var resolved = await SnapshotEnvelopeResolver.ResolveForRestoreAsync(envelope, blob);
        await target.SetResolvedSnapshotAsync(resolved);
        var restored = await target.GetStateAsync();

        Assert.True(restored.IsSuccess);
        Assert.Equal(["custom"], Assert.IsType<CustomStreamPayloadProjector>(restored.GetValue().Payload).Values);
    }

    [Fact]
    public async Task Custom_projector_without_the_optional_capability_uses_exactly_one_logged_fallback()
    {
        const string payloadSecret = "custom-buffered-payload-must-not-be-logged";
        var types = new SimpleMultiProjectorTypes();
        Assert.True(types.RegisterProjectorWithCustomSerialization<CustomBufferedPayloadProjector>().IsSuccess);
        var streamingTypes = Assert.IsAssignableFrom<IStreamingMultiProjectorTypes>(types);
        Assert.False(streamingTypes.SupportsStreamDeserialization(CustomBufferedPayloadProjector.MultiProjectorName));
        var (envelope, blob) = await CreateOffloadedEnvelopeAsync(
            types,
            [payloadSecret],
            CustomBufferedPayloadProjector.MultiProjectorName);
        var logger = new RecordingLogger();
        var target = new GeneralMultiProjectionActor(
            CreateDomain(types),
            CustomBufferedPayloadProjector.MultiProjectorName,
            logger: logger);

        await using var resolved = await SnapshotEnvelopeResolver.ResolveForRestoreAsync(envelope, blob);
        await target.SetResolvedSnapshotAsync(resolved);

        var restored = await target.GetStateAsync();
        Assert.True(restored.IsSuccess);
        Assert.Equal(
            [payloadSecret],
            Assert.IsType<CustomBufferedPayloadProjector>(restored.GetValue().Payload).Values);
        var fallback = Assert.Single(
            logger.Messages,
            message => message.Contains("capability-absent", StringComparison.Ordinal));
        Assert.DoesNotContain(payloadSecret, fallback, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frozen_10_18_external_registry_binary_loads_and_is_invoked_through_the_buffered_compatibility_path()
    {
        // The fixture project references only the published 10.18 packages. At test run its old ICore interface token is
        // bound to this build, proving the optional capability did not add an abstract member or prevent loading.
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Sekiban.Dcb.StreamingRestore.Legacy1018Fixture.dll");
        var loaded = System.Reflection.Assembly.LoadFrom(fixturePath);
        var registryType = loaded.GetType(
            "Sekiban.Dcb.StreamingRestore.Legacy1018Fixture.Legacy1018ExternalRegistry",
            throwOnError: true)!;
        var registry = Assert.IsAssignableFrom<ICoreMultiProjectorTypes>(Activator.CreateInstance(registryType));
        Assert.False(registry is IStreamingMultiProjectorTypes);

        var bytes = Encoding.UTF8.GetBytes("v10.18 external registry payload");
        var blob = new CappedBlobAccessor();
        const string projectorName = "legacy-1018-external-registry";
        const string projectorVersion = "10.18";
        var key = await blob.WriteAsync(new MemoryStream(bytes), projectorName);
        var envelope = new SerializableMultiProjectionStateEnvelope(
            IsOffloaded: true,
            InlineState: null,
            OffloadedState: new SerializableMultiProjectionStateOffloaded(
                key,
                blob.ProviderName,
                "Sekiban.Dcb.StreamingRestore.Legacy1018Fixture.Legacy1018Payload",
                projectorName,
                projectorVersion,
                SortableUniqueId.MinValue.Value,
                Guid.Empty,
                Version: 3,
                IsCatchedUp: true,
                IsSafeState: true,
                PayloadLength: bytes.Length,
                OriginalSizeBytes: bytes.Length,
                CompressedSizeBytes: bytes.Length));
        var actor = new GeneralMultiProjectionActor(
            CreateDomain(registry),
            projectorName);

        await using var resolved = await SnapshotEnvelopeResolver.ResolveForRestoreAsync(envelope, blob);
        await actor.SetResolvedSnapshotAsync(resolved);

        var state = await actor.GetStateAsync();
        Assert.True(state.IsSuccess);
        Assert.Equal(
            "v10.18 external registry payload",
            registryType.Assembly
                .GetType(state.GetValue().Payload.GetType().FullName!, throwOnError: true)!
                .GetProperty("Value")!
                .GetValue(state.GetValue().Payload));
        // Once for the explicitly observable fallback and once for the legacy dual-state clone. Both calls are to the
        // separately compiled 10.18 implementation, not a test double built against the new capability.
        Assert.Equal(2, registryType.GetProperty("DeserializeCalls")!.GetValue(registry));
    }

    [Fact]
    public void Streaming_restore_public_surface_is_additive_and_the_legacy_registry_contract_is_frozen()
    {
        // This list is the published v10.18 ICoreMultiProjectorTypes surface. The stream capability must remain a
        // separate opt-in interface: adding a member here would break the separately compiled external fixture above.
        var legacy = typeof(ICoreMultiProjectorTypes).GetMethods();
        Assert.Equal(12, legacy.Length);
        AssertMethod(legacy, "Project", typeof(ResultBox<IMultiProjectionPayload>),
            typeof(string), typeof(IMultiProjectionPayload), typeof(Event), typeof(List<ITag>),
            typeof(DcbDomainTypes), typeof(SortableUniqueId));
        AssertMethod(legacy, "GetProjectorVersion", typeof(ResultBox<string>), typeof(string));
        AssertMethod(legacy, "GetAllProjectorNames", typeof(IReadOnlyList<string>));
        AssertMethod(legacy, "GetInitialPayloadGenerator", typeof(ResultBox<Func<IMultiProjectionPayload>>), typeof(string));
        AssertMethod(legacy, "GetProjectorType", typeof(ResultBox<Type>), typeof(string));
        AssertMethod(legacy, "GenerateInitialPayload", typeof(ResultBox<IMultiProjectionPayload>), typeof(string));
        AssertMethod(legacy, "Deserialize", typeof(ResultBox<IMultiProjectionPayload>),
            typeof(byte[]), typeof(string), typeof(JsonSerializerOptions));
        AssertMethod(legacy, "Deserialize", typeof(ResultBox<IMultiProjectionPayload>),
            typeof(string), typeof(DcbDomainTypes), typeof(string), typeof(byte[]));
        AssertMethod(legacy, "Serialize", typeof(ResultBox<SerializationResult>),
            typeof(string), typeof(DcbDomainTypes), typeof(string), typeof(IMultiProjectionPayload));
        AssertMethod(legacy, "SerializeToStream", typeof(ResultBox<SerializationSizeInfo>),
            typeof(string), typeof(DcbDomainTypes), typeof(string), typeof(IMultiProjectionPayload), typeof(Stream));
        AssertMethod(legacy, "DeserializeJson", typeof(ResultBox<IMultiProjectionPayload>),
            typeof(string), typeof(string), typeof(DcbDomainTypes));
        var customRegistration = Assert.Single(
            legacy,
            method => method.Name == "RegisterProjectorWithCustomSerialization");
        Assert.True(customRegistration.IsGenericMethodDefinition);
        Assert.Equal(typeof(ResultBox<bool>), customRegistration.ReturnType);
        Assert.Empty(customRegistration.GetParameters());
        Assert.DoesNotContain(legacy, method => method.Name is "DeserializeFromStreamAsync" or "SupportsStreamDeserialization");

        var streaming = typeof(IStreamingMultiProjectorTypes).GetMethods();
        Assert.Equal(2, streaming.Length);
        AssertMethod(streaming, "SupportsStreamDeserialization", typeof(bool), typeof(string));
        AssertMethod(streaming, "DeserializeFromStreamAsync", typeof(Task<ResultBox<IMultiProjectionPayload>>),
            typeof(string), typeof(DcbDomainTypes), typeof(string), typeof(Stream), typeof(CancellationToken));

        var custom = typeof(ICoreMultiProjectorWithStreamDeserialization).GetMethods();
        Assert.Single(custom);
        AssertMethod(custom, "DeserializeFromStreamAsync", typeof(Task<IMultiProjectionPayload>),
            typeof(DcbDomainTypes), typeof(string), typeof(Stream), typeof(CancellationToken));

        Assert.Equal(
            ["IsOffloaded", "PayloadStream", "State"],
            typeof(ResolvedSnapshotRestore).GetProperties().Select(property => property.Name).OrderBy(name => name));
        Assert.Contains(typeof(IAsyncDisposable), typeof(ResolvedSnapshotRestore).GetInterfaces());
        AssertMethod(
            typeof(SnapshotEnvelopeResolver).GetMethods(),
            "ResolveForRestoreAsync",
            typeof(Task<ResolvedSnapshotRestore>),
            typeof(SerializableMultiProjectionStateEnvelope), typeof(IBlobStorageSnapshotAccessor), typeof(CancellationToken));
        AssertMethod(
            typeof(GeneralMultiProjectionActor).GetMethods(),
            "SetResolvedSnapshotAsync",
            typeof(Task),
            typeof(ResolvedSnapshotRestore), typeof(CancellationToken));
    }

    [Fact]
    [Trait("Category", "StreamingRestoreNormalMemory")]
    public async Task Controlled_small_graph_large_wire_offloaded_gzip_fixture_uses_the_same_nonbuffering_production_seam()
    {
        // Normal CI fixture: this is intentionally 16-32 MiB before gzip. It proves the selected path and stream
        // structure; it does not claim that the projection graph itself cannot OOM.
        var (envelope, blob, uncompressedWireBytes) = await CreateSmallGraphLargeWireFixtureAsync();
        Assert.InRange(uncompressedWireBytes, 16 * 1024 * 1024, 32 * 1024 * 1024);
        var targetTypes = new CountingStreamingRegistry(CreateReflectionRegistry());
        var target = CreateActor(targetTypes);

        await using var resolved = await SnapshotEnvelopeResolver.ResolveForRestoreAsync(envelope, blob);
        var stream = Assert.IsType<CappedNonSeekableStream>(resolved.PayloadStream);
        await target.SetResolvedSnapshotAsync(resolved);

        Assert.Equal(2, targetTypes.StreamDeserializeCalls);
        Assert.Equal(0, targetTypes.BufferedDeserializeCalls);
        Assert.True(stream.ReadCalls > 1);
        Assert.Equal(0, stream.LengthAccesses);
        Assert.Equal(0, stream.SeekCalls);
        Assert.Equal(0, GetLastStreamingRestoreWholePayloadAggregationCount(target));

        // Same envelope, same resolver-to-actor production seam, but a deliberately buffered capability. The shared
        // actor-side observation — not the registry's test counter — must expose the whole-payload aggregation.
        var bufferedControlTypes = new IntentionallyBufferedStreamingRegistry(CreateReflectionRegistry());
        var bufferedControl = CreateActor(bufferedControlTypes);
        await using var bufferedResolved = await SnapshotEnvelopeResolver.ResolveForRestoreAsync(envelope, blob);
        await bufferedControl.SetResolvedSnapshotAsync(bufferedResolved);
        Assert.Equal(2, bufferedControlTypes.WholePayloadAggregations);
        Assert.Equal(2, GetLastStreamingRestoreWholePayloadAggregationCount(bufferedControl));
    }

    [Fact]
    [Trait("Category", "StreamingRestoreManualMemory")]
    public async Task Manual_143MiB_offloaded_fixture_reports_peak_and_selected_restore_path_without_claiming_no_oom()
    {
        // This intentionally does not run as part of ordinary CI. The dedicated scheduled/manual workflow supplies the
        // opt-in variable, runs this test in its own process with a timeout and virtual-memory ceiling, and preserves
        // the console telemetry. The assertion is selected-path structure, never a "no OOM" promise.
        if (!string.Equals(Environment.GetEnvironmentVariable("SEKIBAN_STREAM_RESTORE_SMOKE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        const int inputBytes = 112_500_000; // Base64 JSON payload ~= 143 MiB; within the 128-200 MiB dedicated band.
        var stopwatch = Stopwatch.StartNew();
        var process = Process.GetCurrentProcess();
        var peakBefore = process.PeakWorkingSet64;
        var largeBytes = new byte[inputBytes];
        Random.Shared.NextBytes(largeBytes);
        var largeValue = Convert.ToBase64String(largeBytes);
        var sourceTypes = CreateReflectionRegistry();
        var (envelope, blob) = await CreateOffloadedEnvelopeAsync(sourceTypes, [largeValue]);
        var targetTypes = new CountingStreamingRegistry(CreateReflectionRegistry());
        var target = CreateActor(targetTypes);

        await using var resolved = await SnapshotEnvelopeResolver.ResolveForRestoreAsync(envelope, blob);
        var stream = Assert.IsType<CappedNonSeekableStream>(resolved.PayloadStream);
        await target.SetResolvedSnapshotAsync(resolved);
        stopwatch.Stop();
        process.Refresh();

        Console.WriteLine(
            $"SEK-G42 streaming-restore telemetry: selected=IStreamingMultiProjectorTypes; " +
            $"payloadBandMiB=143; elapsedMs={stopwatch.ElapsedMilliseconds}; " +
            $"peakWorkingSetBytes={process.PeakWorkingSet64}; peakDeltaBytes={process.PeakWorkingSet64 - peakBefore}; " +
            $"streamDeserializeCalls={targetTypes.StreamDeserializeCalls}; bufferedDeserializeCalls={targetTypes.BufferedDeserializeCalls}; " +
            $"readCalls={stream.ReadCalls}; lengthAccesses={stream.LengthAccesses}; seekCalls={stream.SeekCalls}");

        Assert.Equal(0, GetLastStreamingRestoreWholePayloadAggregationCount(target));
        Assert.True(stream.ReadCalls > 1);
        Assert.Equal(0, stream.LengthAccesses);
        Assert.Equal(0, stream.SeekCalls);
    }

    private static SimpleMultiProjectorTypes CreateReflectionRegistry()
    {
        var types = new SimpleMultiProjectorTypes();
        types.RegisterProjector<StreamPayloadProjector>();
        return types;
    }

    private static void AssertMethod(
        IEnumerable<System.Reflection.MethodInfo> methods,
        string name,
        Type returnType,
        params Type[] parameterTypes)
    {
        var method = Assert.Single(methods, candidate =>
            candidate.Name == name &&
            candidate.ReturnType == returnType &&
            candidate.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameterTypes));
        Assert.True(method.IsPublic);
    }

    private static GeneralMultiProjectionActor CreateActor(
        ICoreMultiProjectorTypes types,
        ILogger? logger = null) =>
        new(CreateDomain(types), StreamPayloadProjector.MultiProjectorName, logger: logger);

    private static DcbDomainTypes CreateDomain(ICoreMultiProjectorTypes types)
    {
        var events = new SimpleEventTypes();
        events.RegisterEventType<PayloadAdded>(nameof(PayloadAdded));
        return new DcbDomainTypes(
            events,
            new SimpleTagTypes(),
            new SimpleTagProjectorTypes(),
            new SimpleTagStatePayloadTypes(),
            types,
            new SimpleQueryTypes());
    }

    private static async Task<(SerializableMultiProjectionStateEnvelope Envelope, CappedBlobAccessor Blob)>
        CreateOffloadedEnvelopeAsync(
            ICoreMultiProjectorTypes types,
            IReadOnlyList<string> values,
            string? projectorName = null)
    {
        var blob = new CappedBlobAccessor();
        var selectedProjectorName = projectorName ?? StreamPayloadProjector.MultiProjectorName;
        var actor = new GeneralMultiProjectionActor(CreateDomain(types), selectedProjectorName);
        foreach (var value in values)
        {
            await actor.AddEventsAsync([CreateEvent(value)]);
        }

        var result = await actor.BuildSnapshotEnvelopeAsync(
            canGetUnsafeState: true,
            blobAccessor: blob,
            offloadThresholdBytes: 1);
        Assert.True(result.IsSuccess);
        Assert.True(result.GetValue().IsOffloaded);
        return (result.GetValue(), blob);
    }

    private static Event CreateEvent(string value) => new(
        new PayloadAdded(value),
        SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid()),
        nameof(PayloadAdded),
        Guid.NewGuid(),
        new EventMetadata(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "test"),
        []);

    private static IReadOnlyList<string> Payload(MultiProjectionState state) =>
        Assert.IsType<StreamPayloadProjector>(state.Payload).Values;

    public sealed record PayloadAdded(string Value) : IEventPayload;

    public sealed record StreamPayloadProjector(List<string> Values) : IMultiProjector<StreamPayloadProjector>
    {
        public StreamPayloadProjector() : this([]) { }
        public static string MultiProjectorName => "streaming-restore";
        public static string MultiProjectorVersion => "1";
        public static StreamPayloadProjector GenerateInitialPayload() => new([]);

        public static ResultBox<StreamPayloadProjector> Project(
            StreamPayloadProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) =>
            ev.Payload is PayloadAdded added
                ? ResultBox.FromValue(payload with { Values = [.. payload.Values, added.Value] })
                : ResultBox.FromValue(payload);
    }

    public sealed record CustomStreamPayloadProjector(List<string> Values) :
        ICoreMultiProjectorWithCustomSerialization<CustomStreamPayloadProjector>,
        ICoreMultiProjectorWithStreamDeserialization
    {
        public CustomStreamPayloadProjector() : this([]) { }
        public static string MultiProjectorName => "streaming-restore-custom";
        public static string MultiProjectorVersion => "1";
        public static CustomStreamPayloadProjector GenerateInitialPayload() => new([]);

        public static ResultBox<CustomStreamPayloadProjector> Project(
            CustomStreamPayloadProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) =>
            ev.Payload is PayloadAdded added
                ? ResultBox.FromValue(payload with { Values = [.. payload.Values, added.Value] })
                : ResultBox.FromValue(payload);

        public static SerializationResult Serialize(
            DcbDomainTypes domainTypes,
            string safeWindowThreshold,
            CustomStreamPayloadProjector payload)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, domainTypes.JsonSerializerOptions);
            return new SerializationResult(bytes, bytes.LongLength, bytes.LongLength);
        }

        public static CustomStreamPayloadProjector Deserialize(
            DcbDomainTypes domainTypes,
            string safeWindowThreshold,
            ReadOnlySpan<byte> data) =>
            JsonSerializer.Deserialize<CustomStreamPayloadProjector>(data, domainTypes.JsonSerializerOptions)
            ?? throw new InvalidDataException("custom stream payload was empty");

        public async Task<IMultiProjectionPayload> DeserializeFromStreamAsync(
            DcbDomainTypes domainTypes,
            string safeWindowThreshold,
            Stream source,
            CancellationToken cancellationToken = default) =>
            await JsonSerializer.DeserializeAsync<CustomStreamPayloadProjector>(
                    source,
                    domainTypes.JsonSerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false)
            ?? throw new InvalidDataException("custom stream payload was empty");
    }

    public sealed record CustomBufferedPayloadProjector(List<string> Values) :
        ICoreMultiProjectorWithCustomSerialization<CustomBufferedPayloadProjector>
    {
        public CustomBufferedPayloadProjector() : this([]) { }
        public static string MultiProjectorName => "streaming-restore-custom-buffered";
        public static string MultiProjectorVersion => "1";
        public static CustomBufferedPayloadProjector GenerateInitialPayload() => new([]);

        public static ResultBox<CustomBufferedPayloadProjector> Project(
            CustomBufferedPayloadProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) =>
            ev.Payload is PayloadAdded added
                ? ResultBox.FromValue(payload with { Values = [.. payload.Values, added.Value] })
                : ResultBox.FromValue(payload);

        public static SerializationResult Serialize(
            DcbDomainTypes domainTypes,
            string safeWindowThreshold,
            CustomBufferedPayloadProjector payload)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, domainTypes.JsonSerializerOptions);
            return new SerializationResult(bytes, bytes.LongLength, bytes.LongLength);
        }

        public static CustomBufferedPayloadProjector Deserialize(
            DcbDomainTypes domainTypes,
            string safeWindowThreshold,
            ReadOnlySpan<byte> data) =>
            JsonSerializer.Deserialize<CustomBufferedPayloadProjector>(data, domainTypes.JsonSerializerOptions)
            ?? throw new InvalidDataException("custom buffered payload was empty");
    }

    [JsonSerializable(typeof(StreamPayloadProjector))]
    private sealed partial class StreamingRestoreJsonContext : JsonSerializerContext;

    private abstract class DelegatingRegistry : ICoreMultiProjectorTypes
    {
        protected DelegatingRegistry(ICoreMultiProjectorTypes inner) => Inner = inner;
        protected ICoreMultiProjectorTypes Inner { get; }
        public int BufferedDeserializeCalls { get; protected set; }

        public ResultBox<IMultiProjectionPayload> Project(string name, IMultiProjectionPayload payload, Event ev,
            List<ITag> tags, DcbDomainTypes domain, SortableUniqueId threshold) =>
            Inner.Project(name, payload, ev, tags, domain, threshold);
        public ResultBox<string> GetProjectorVersion(string name) => Inner.GetProjectorVersion(name);
        public IReadOnlyList<string> GetAllProjectorNames() => Inner.GetAllProjectorNames();
        public ResultBox<Func<IMultiProjectionPayload>> GetInitialPayloadGenerator(string name) =>
            Inner.GetInitialPayloadGenerator(name);
        public ResultBox<Type> GetProjectorType(string name) => Inner.GetProjectorType(name);
        public ResultBox<IMultiProjectionPayload> GenerateInitialPayload(string name) => Inner.GenerateInitialPayload(name);
        public ResultBox<IMultiProjectionPayload> Deserialize(byte[] data, string name, JsonSerializerOptions options) =>
            Inner.Deserialize(data, name, options);
        public ResultBox<SerializationResult> Serialize(string name, DcbDomainTypes domain, string threshold,
            IMultiProjectionPayload payload) => Inner.Serialize(name, domain, threshold, payload);
        public virtual ResultBox<IMultiProjectionPayload> Deserialize(string name, DcbDomainTypes domain, string threshold,
            byte[] data)
        {
            BufferedDeserializeCalls++;
            return Inner.Deserialize(name, domain, threshold, data);
        }
        public ResultBox<bool> RegisterProjectorWithCustomSerialization<T>()
            where T : ICoreMultiProjectorWithCustomSerialization<T>, new() =>
            Inner.RegisterProjectorWithCustomSerialization<T>();
    }

    private sealed class CountingBufferedRegistry(ICoreMultiProjectorTypes inner) : DelegatingRegistry(inner);

    private class CountingStreamingRegistry(ICoreMultiProjectorTypes inner) : DelegatingRegistry(inner), IStreamingMultiProjectorTypes
    {
        private readonly IStreamingMultiProjectorTypes _streaming = Assert.IsAssignableFrom<IStreamingMultiProjectorTypes>(inner);
        public int StreamDeserializeCalls { get; protected set; }
        public bool SupportsStreamDeserialization(string projectorName) => _streaming.SupportsStreamDeserialization(projectorName);

        public virtual async Task<ResultBox<IMultiProjectionPayload>> DeserializeFromStreamAsync(
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

    private sealed class FailingStreamingRegistry(ICoreMultiProjectorTypes inner) : CountingStreamingRegistry(inner)
    {
        public override async Task<ResultBox<IMultiProjectionPayload>> DeserializeFromStreamAsync(
            string projectorName,
            DcbDomainTypes domainTypes,
            string safeWindowThreshold,
            Stream source,
            CancellationToken cancellationToken = default)
        {
            StreamDeserializeCalls++;
            await Task.CompletedTask;
            return ResultBox.Error<IMultiProjectionPayload>(new InvalidDataException("stream-capability-failure"));
        }
    }

    private sealed class IntentionallyBufferedStreamingRegistry(ICoreMultiProjectorTypes inner) : DelegatingRegistry(inner), IStreamingMultiProjectorTypes
    {
        private readonly IStreamingMultiProjectorTypes _streaming = Assert.IsAssignableFrom<IStreamingMultiProjectorTypes>(inner);
        public int StreamDeserializeCalls { get; private set; }
        public int WholePayloadAggregations { get; private set; }

        public bool SupportsStreamDeserialization(string projectorName) => _streaming.SupportsStreamDeserialization(projectorName);

        public async Task<ResultBox<IMultiProjectionPayload>> DeserializeFromStreamAsync(
            string projectorName,
            DcbDomainTypes domainTypes,
            string safeWindowThreshold,
            Stream source,
            CancellationToken cancellationToken = default)
        {
            StreamDeserializeCalls++;
            WholePayloadAggregations++;
            var bytes = await StreamReadHelper.ReadAllBytesAsync(source, cancellationToken);
            return Inner.Deserialize(projectorName, domainTypes, safeWindowThreshold, bytes);
        }
    }

    private sealed class CappedBlobAccessor : IBlobStorageSnapshotAccessor
    {
        private readonly Dictionary<string, byte[]> _payloads = new(StringComparer.Ordinal);
        private int _nextKey;
        public string ProviderName => "capped-test";
        public byte[]? LastWrittenPayload { get; private set; }
        public int? TruncateOpenedPayloadTo { get; set; }
        public byte[]? OpenedPayloadPrefix { get; set; }
        public byte[]? OpenedPayloadOverride { get; set; }

        public async Task<string> WriteAsync(Stream data, string projectorName, CancellationToken cancellationToken = default)
        {
            using var copy = new MemoryStream();
            await data.CopyToAsync(copy, cancellationToken);
            var key = $"{projectorName}/{++_nextKey}";
            LastWrittenPayload = copy.ToArray();
            _payloads[key] = LastWrittenPayload;
            return key;
        }

        public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
        {
            var payload = OpenedPayloadOverride ?? _payloads[key];
            if (TruncateOpenedPayloadTo is { } truncated)
            {
                payload = payload[..Math.Min(payload.Length, truncated)];
            }

            var initialOffset = 0;
            if (OpenedPayloadPrefix is { Length: > 0 } prefix)
            {
                var prefixed = new byte[prefix.Length + payload.Length];
                prefix.CopyTo(prefixed, 0);
                payload.CopyTo(prefixed, prefix.Length);
                payload = prefixed;
                initialOffset = prefix.Length;
            }

            return Task.FromResult<Stream>(new CappedNonSeekableStream(payload, maxReadBytes: 127, initialOffset));
        }
    }

    private sealed class CappedNonSeekableStream : Stream
    {
        private readonly MemoryStream _inner;
        private readonly int _maxReadBytes;
        public CappedNonSeekableStream(byte[] payload, int maxReadBytes, int initialOffset = 0)
        {
            _inner = new MemoryStream(payload, writable: false);
            _maxReadBytes = maxReadBytes;
            _inner.Position = initialOffset;
        }

        public int ReadCalls { get; private set; }
        public int LengthAccesses { get; private set; }
        public int SeekCalls { get; private set; }
        public bool IsDisposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length
        {
            get
            {
                LengthAccesses++;
                throw new NotSupportedException("Length is forbidden for this production-stream test.");
            }
        }
        public override long Position
        {
            get => throw new NotSupportedException("Position is forbidden for this production-stream test.");
            set => throw new NotSupportedException("Position is forbidden for this production-stream test.");
        }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            ReadCalls++;
            return _inner.Read(buffer[..Math.Min(buffer.Length, _maxReadBytes)]);
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCalls++;
            return await _inner.ReadAsync(buffer[..Math.Min(buffer.Length, _maxReadBytes)], cancellationToken);
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            SeekCalls++;
            throw new NotSupportedException("Seek is forbidden for this production-stream test.");
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

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
