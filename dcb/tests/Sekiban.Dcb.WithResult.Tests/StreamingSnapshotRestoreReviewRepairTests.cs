using System.Collections.Concurrent;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Tests;

public sealed partial class StreamingSnapshotRestoreTests
{
    // v9 carries JSON text, so gzip bytes cannot be represented in its PayloadJson field. Every representable route is
    // enumerated here instead of sampling separate one-off examples: 3 registries x (raw v9, raw v10, gzip v10) x
    // (offloaded stream guarantee, inline byte compatibility fallback).
    public static IEnumerable<object[]> StreamingRestoreEquivalenceMatrix()
    {
        foreach (var registry in Enum.GetValues<MatrixRegistryKind>())
        {
            foreach (var delivery in Enum.GetValues<MatrixDelivery>())
            {
                yield return [new MatrixCell(registry, MatrixPayloadFormat.RawLegacyJson, MatrixWireVersion.V9, delivery)];
                yield return [new MatrixCell(registry, MatrixPayloadFormat.RawLegacyJson, MatrixWireVersion.V10, delivery)];
                yield return [new MatrixCell(registry, MatrixPayloadFormat.GzipJson, MatrixWireVersion.V10, delivery)];
            }
        }
    }

    [Theory]
    [MemberData(nameof(StreamingRestoreEquivalenceMatrix))]
    public async Task Stream_and_legacy_restore_paths_keep_payload_safe_threshold_and_tracking_metadata_equivalent(
        MatrixCell cell)
    {
        const int expectedVersion = 701;
        var expectedLastEventId = Guid.Parse("71b3e3a3-29a3-4935-ae64-2ac07b06d582");
        var expectedLastSortableUniqueId = SortableUniqueId.Generate(
            new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            Guid.Parse("e266a43d-b220-414b-8da2-6cb9c7d4009a"));
        var expectedValue = $"matrix-{cell.Registry}-{cell.PayloadFormat}-{cell.WireVersion}-{cell.Delivery}";

        var byteSetup = CreateMatrixRegistry(cell.Registry);
        var byteDomain = CreateDomain(byteSetup.Registry);
        var payloadBytes = CreateMatrixPayloadBytes(cell, byteDomain, expectedValue);
        var legacyState = CreateMatrixState(
            cell,
            payloadBytes,
            byteSetup.ProjectorName,
            byteSetup.PayloadType,
            expectedLastSortableUniqueId,
            expectedLastEventId,
            expectedVersion);
        var legacyActor = new GeneralMultiProjectionActor(CreateDomain(byteSetup.Registry), byteSetup.ProjectorName);

        MatrixCustomPayloadProjector.ResetRecordedSafeThresholds();
        var legacySelectedThreshold = await RestoreInlineAndCaptureSelectedThresholdAsync(
            legacyActor,
            legacyState);
        var legacy = await legacyActor.GetStateAsync(canGetUnsafeState: true);
        Assert.True(legacy.IsSuccess);
        Assert.Equal(0, byteSetup.StreamDeserializeCalls);
        // Each path owns an actor and therefore selects its own timestamp-derived threshold. Exact equality is against
        // that actor's one recorded selection, not a wall-clock range shared by two independently constructed actors.
        // A recomputation for either byte-path clone or either stream-path clone is consequently observable.
        AssertRecordedThresholdsExactly(
            byteSetup.BufferedSafeThresholds,
            legacySelectedThreshold,
            expectedCount: 2,
            "legacy byte path");
        AssertCustomThresholdsExactly(cell.Registry, legacySelectedThreshold, expectedCount: 2, "legacy byte path");

        var restoreSetup = CreateMatrixRegistry(cell.Registry);
        var restoreActor = new GeneralMultiProjectionActor(CreateDomain(restoreSetup.Registry), restoreSetup.ProjectorName);
        MatrixCustomPayloadProjector.ResetRecordedSafeThresholds();
        string restoreSelectedThreshold;

        if (cell.Delivery == MatrixDelivery.Offloaded)
        {
            var blob = new CappedBlobAccessor();
            var key = await blob.WriteAsync(new MemoryStream(payloadBytes, writable: false), restoreSetup.ProjectorName);
            var envelope = new SerializableMultiProjectionStateEnvelope(
                IsOffloaded: true,
                InlineState: null,
                OffloadedState: new SerializableMultiProjectionStateOffloaded(
                    key,
                    blob.ProviderName,
                    restoreSetup.PayloadType,
                    restoreSetup.ProjectorName,
                    MatrixProjectorVersion,
                    expectedLastSortableUniqueId,
                    expectedLastEventId,
                    expectedVersion,
                    IsCatchedUp: false,
                    IsSafeState: true,
                    PayloadLength: payloadBytes.Length,
                    OriginalSizeBytes: payloadBytes.Length,
                    CompressedSizeBytes: payloadBytes.Length));

            await using var resolved = await SnapshotEnvelopeResolver.ResolveForRestoreAsync(envelope, blob);
            restoreSelectedThreshold = await RestoreStreamAndCaptureSelectedThresholdAsync(restoreActor, resolved);
            Assert.Equal(2, restoreSetup.StreamDeserializeCalls);
            Assert.Empty(restoreSetup.BufferedSafeThresholds);
            AssertRecordedThresholdsExactly(
                restoreSetup.StreamSafeThresholds,
                restoreSelectedThreshold,
                expectedCount: 2,
                "offloaded stream path");
        }
        else
        {
            restoreSelectedThreshold = await RestoreInlineAndCaptureSelectedThresholdAsync(restoreActor, legacyState);
            Assert.Equal(0, restoreSetup.StreamDeserializeCalls);
            AssertRecordedThresholdsExactly(
                restoreSetup.BufferedSafeThresholds,
                restoreSelectedThreshold,
                expectedCount: 2,
                "inline compatibility path");
        }

        AssertCustomThresholdsExactly(
            cell.Registry,
            restoreSelectedThreshold,
            expectedCount: 2,
            cell.Delivery == MatrixDelivery.Offloaded ? "offloaded stream path" : "inline compatibility path");

        var restored = await restoreActor.GetStateAsync(canGetUnsafeState: true);
        Assert.True(restored.IsSuccess);
        Assert.Equal(expectedValue, GetMatrixPayloadValue(restored.GetValue().Payload));
        Assert.Equal(GetMatrixPayloadValue(legacy.GetValue().Payload), GetMatrixPayloadValue(restored.GetValue().Payload));
        Assert.Equal(legacy.GetValue().Version, restored.GetValue().Version);
        Assert.Equal(expectedVersion, restored.GetValue().Version);
        Assert.Equal(legacy.GetValue().LastEventId, restored.GetValue().LastEventId);
        Assert.Equal(expectedLastEventId, restored.GetValue().LastEventId);
        Assert.Equal(legacy.GetValue().LastSortableUniqueId, restored.GetValue().LastSortableUniqueId);
        Assert.Equal(expectedLastSortableUniqueId, restored.GetValue().LastSortableUniqueId);
        Assert.Equal(legacy.GetValue().IsCatchedUp, restored.GetValue().IsCatchedUp);
        Assert.False(restored.GetValue().IsCatchedUp);
        Assert.Equal(legacy.GetValue().IsSafeState, restored.GetValue().IsSafeState);
    }

    [Fact]
    public void Equivalence_matrix_explicitly_covers_every_representable_registry_format_version_and_delivery_cell()
    {
        var cells = StreamingRestoreEquivalenceMatrix().Select(row => Assert.IsType<MatrixCell>(row[0])).ToArray();
        Assert.Equal(18, cells.Length);

        foreach (var registry in Enum.GetValues<MatrixRegistryKind>())
        {
            foreach (var delivery in Enum.GetValues<MatrixDelivery>())
            {
                Assert.Contains(cells, cell => cell == new MatrixCell(
                    registry,
                    MatrixPayloadFormat.RawLegacyJson,
                    MatrixWireVersion.V9,
                    delivery));
                Assert.Contains(cells, cell => cell == new MatrixCell(
                    registry,
                    MatrixPayloadFormat.RawLegacyJson,
                    MatrixWireVersion.V10,
                    delivery));
                Assert.Contains(cells, cell => cell == new MatrixCell(
                    registry,
                    MatrixPayloadFormat.GzipJson,
                    MatrixWireVersion.V10,
                    delivery));
            }
        }

        Assert.DoesNotContain(cells, cell =>
            cell.PayloadFormat == MatrixPayloadFormat.GzipJson && cell.WireVersion == MatrixWireVersion.V9);
    }

    [Fact]
    public void Restore_path_selects_one_safe_threshold_and_cannot_recompute_it_in_a_helper()
    {
        // The exact matrix assertions prove the values received by primary and clone. This complementary structural
        // pin prevents a later actor helper from silently asking the wall clock for a second, in-window threshold.
        var actorType = typeof(GeneralMultiProjectionActor);
        var restorePayload = actorType.GetMethod(
            "RestorePayloadAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var restoreDirectCalls = EnumerateDirectReferencedMembers(GetImplementationMethod(restorePayload))
            .OfType<MethodInfo>()
            .Where(method => method.DeclaringType == actorType)
            .ToArray();
        Assert.Equal(
            1,
            restoreDirectCalls.Count(method => method.Name == "SelectSnapshotRestoreSafeWindowThreshold"));

        var allActorSafeThresholdCalls = EnumerateTransitiveReferencedMembers([restorePayload])
            .OfType<MethodInfo>()
            .Where(method =>
                method.DeclaringType == actorType &&
                method.Name == "GetSafeWindowThreshold")
            .ToArray();
        Assert.Single(allActorSafeThresholdCalls);

        var streamPair = actorType.GetMethod(
            "DeserializeStreamingPayloadPairAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.DoesNotContain(
            EnumerateTransitiveReferencedMembers([streamPair]).OfType<MethodInfo>(),
            method => method.DeclaringType == actorType && method.Name == "GetSafeWindowThreshold");
    }

    [Fact]
    public void Supported_stream_restore_transitive_path_has_no_whole_payload_aggregation_api_reference()
    {
        // This is a production structural pin, not read-size telemetry. The capability-present actor branch is a
        // deliberately closed inventory because RestorePayloadAsync also owns the allowed legacy byte fallback. From
        // these roots, the scanner follows every method in Core/Core.Model, including async state machines, so a
        // whole-payload helper moved into a registry forwarder, tee, or prefix wrapper cannot escape the proof.
        var roots = GetSupportedStreamRestorePathRoots();
        var teeType = GetSnapshotRestoreTeeReadStreamType();
        var prefixType = GetPrefixBufferedStreamType();
        var supportType = GetStreamingMultiProjectorTypesSupportType();

        Assert.Contains(roots, method =>
            method.DeclaringType == typeof(GeneralMultiProjectionActor) &&
            method.Name == "DeserializeStreamingPayloadPairAsync");
        Assert.Contains(roots, method =>
            method.DeclaringType == typeof(SnapshotEnvelopeResolver) &&
            method.Name == nameof(SnapshotEnvelopeResolver.ResolveForRestoreAsync));
        Assert.Contains(roots, method => method.DeclaringType == teeType && method.Name == nameof(Stream.ReadAsync));
        Assert.Contains(roots, method => method.DeclaringType == prefixType && method.Name == "CreateAsync");
        Assert.Contains(roots, method => method.DeclaringType == supportType && method.Name == "DeserializeAsync");

        AssertNoWholePayloadAggregationReferences(roots);
    }

    [Fact]
    public void Transitive_aggregation_scan_rejects_each_forbidden_call_hidden_in_async_helpers()
    {
        // Verify the verifier itself. The async root has no direct aggregation call: each prohibited operation sits in
        // a private helper (and ReadAllBytesAsync sits behind a second async state machine). A direct-only scan would
        // miss exactly the mutations this production guard is intended to reject.
        var root = typeof(StreamingSnapshotRestoreTests).GetMethod(
            nameof(AggregationVerifierAsyncRoot),
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var forbidden = FindWholePayloadAggregationReferences([root]);

        Assert.Contains(forbidden, member => member.DeclaringType == typeof(MemoryStream) && member.Name == ".ctor");
        Assert.Contains(forbidden, member => member.DeclaringType == typeof(MemoryStream) && member.Name == "ToArray");
        Assert.Contains(forbidden, member =>
            member.DeclaringType == typeof(File) &&
            member.Name.StartsWith("ReadAllBytes", StringComparison.Ordinal));
        Assert.Contains(forbidden, member =>
            member.DeclaringType == typeof(SerializableMultiProjectionState) &&
            member.Name == nameof(SerializableMultiProjectionState.GetPayloadBytes));
    }

    [Fact]
    public void Stream_restore_public_inventory_pins_all_additive_capability_implementations_and_restore_shape()
    {
        AssertStreamingCapabilityInterfaceSurfaces();
        AssertStreamDeserializerSurface();

        AssertStreamingRegistrySurface(typeof(AotMultiProjectorTypes));
        AssertStreamingRegistrySurface(typeof(SimpleMultiProjectorTypes));
        // The core registry is internal, yet it is the implementation used by the actor's direct core path. Keep it in
        // the inventory too, so every Simple registry is held to the same capability forwarding shape.
        AssertStreamingRegistrySurface(
            typeof(GeneralMultiProjectionActor).Assembly.GetType(
                "Sekiban.Dcb.Domains.SimpleMultiProjectorTypes",
                throwOnError: true)!);

        var restore = typeof(ResolvedSnapshotRestore);
        Assert.True(restore.IsPublic);
        Assert.True(restore.IsSealed);
        Assert.False(restore.IsAbstract);
        Assert.Equal(
            ["IsOffloaded", "PayloadStream", "State"],
            restore.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Select(property => property.Name)
                .OrderBy(name => name));
        Assert.All(
            restore.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
            property =>
            {
                Assert.True(property.CanRead);
                Assert.False(property.CanWrite);
            });
        Assert.Equal(typeof(SerializableMultiProjectionState), restore.GetProperty("State")!.PropertyType);
        Assert.Equal(typeof(Stream), restore.GetProperty("PayloadStream")!.PropertyType);
        Assert.Equal(typeof(bool), restore.GetProperty("IsOffloaded")!.PropertyType);
        Assert.Empty(restore.GetConstructors(BindingFlags.Instance | BindingFlags.Public));
        var constructor = Assert.Single(restore.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.True(constructor.IsAssembly);
        Assert.Equal(
            [typeof(SerializableMultiProjectionState), typeof(Stream), typeof(bool)],
            constructor.GetParameters().Select(parameter => parameter.ParameterType));
        AssertMethod(
            restore.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
            nameof(IDisposable.Dispose),
            typeof(void));
        AssertMethod(
            restore.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
            nameof(IAsyncDisposable.DisposeAsync),
            typeof(ValueTask));
        Assert.Contains(typeof(IDisposable), restore.GetInterfaces());
        Assert.Contains(typeof(IAsyncDisposable), restore.GetInterfaces());

        var restoreMethods = restore.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => (method.Name, method.ReturnType))
            .OrderBy(method => method.Name)
            .ToArray();
        Assert.Equal(
            [(nameof(IDisposable.Dispose), typeof(void)), (nameof(IAsyncDisposable.DisposeAsync), typeof(ValueTask))],
            restoreMethods);

        var resolverMethod = Assert.Single(
            typeof(SnapshotEnvelopeResolver).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly),
            method => method.Name == nameof(SnapshotEnvelopeResolver.ResolveForRestoreAsync));
        Assert.Equal(typeof(Task<ResolvedSnapshotRestore>), resolverMethod.ReturnType);
        Assert.Equal(
            [typeof(SerializableMultiProjectionStateEnvelope), typeof(IBlobStorageSnapshotAccessor), typeof(CancellationToken)],
            resolverMethod.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(
            ["envelope", "blobAccessor", "cancellationToken"],
            resolverMethod.GetParameters().Select(parameter => parameter.Name));
        Assert.True(resolverMethod.GetParameters()[2].IsOptional);

        var actorMethod = Assert.Single(
            typeof(GeneralMultiProjectionActor).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
            method => method.Name == nameof(GeneralMultiProjectionActor.SetResolvedSnapshotAsync));
        Assert.Equal(typeof(Task), actorMethod.ReturnType);
        Assert.Equal(
            [typeof(ResolvedSnapshotRestore), typeof(CancellationToken)],
            actorMethod.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(
            ["snapshot", "cancellationToken"],
            actorMethod.GetParameters().Select(parameter => parameter.Name));
        Assert.True(actorMethod.GetParameters()[1].IsOptional);
    }

    private static void AssertStreamingCapabilityInterfaceSurfaces()
    {
        var registryCapability = typeof(IStreamingMultiProjectorTypes);
        Assert.True(registryCapability.IsPublic);
        var supports = Assert.Single(
            registryCapability.GetMethods(),
            method => method.Name == nameof(IStreamingMultiProjectorTypes.SupportsStreamDeserialization));
        Assert.Equal(typeof(bool), supports.ReturnType);
        Assert.Equal([typeof(string)], supports.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(["projectorName"], supports.GetParameters().Select(parameter => parameter.Name));

        var registryDeserialize = Assert.Single(
            registryCapability.GetMethods(),
            method => method.Name == nameof(IStreamingMultiProjectorTypes.DeserializeFromStreamAsync));
        Assert.Equal(typeof(Task<ResultBox<IMultiProjectionPayload>>), registryDeserialize.ReturnType);
        Assert.Equal(
            [typeof(string), typeof(DcbDomainTypes), typeof(string), typeof(Stream), typeof(CancellationToken)],
            registryDeserialize.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(
            ["projectorName", "domainTypes", "safeWindowThreshold", "source", "cancellationToken"],
            registryDeserialize.GetParameters().Select(parameter => parameter.Name));
        Assert.True(registryDeserialize.GetParameters()[4].IsOptional);

        var projectorCapability = typeof(ICoreMultiProjectorWithStreamDeserialization);
        Assert.True(projectorCapability.IsPublic);
        var projectorDeserialize = Assert.Single(projectorCapability.GetMethods());
        Assert.Equal(nameof(ICoreMultiProjectorWithStreamDeserialization.DeserializeFromStreamAsync), projectorDeserialize.Name);
        Assert.Equal(typeof(Task<IMultiProjectionPayload>), projectorDeserialize.ReturnType);
        Assert.Equal(
            [typeof(DcbDomainTypes), typeof(string), typeof(Stream), typeof(CancellationToken)],
            projectorDeserialize.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(
            ["domainTypes", "safeWindowThreshold", "source", "cancellationToken"],
            projectorDeserialize.GetParameters().Select(parameter => parameter.Name));
        Assert.True(projectorDeserialize.GetParameters()[3].IsOptional);
    }

    private static async Task<string> RestoreInlineAndCaptureSelectedThresholdAsync(
        GeneralMultiProjectionActor actor,
        SerializableMultiProjectionState state)
    {
        await actor.SetSnapshotAsync(new SerializableMultiProjectionStateEnvelope(false, state, null));
        return GetLastSnapshotRestoreSafeWindowThreshold(actor);
    }

    private static async Task<string> RestoreStreamAndCaptureSelectedThresholdAsync(
        GeneralMultiProjectionActor actor,
        ResolvedSnapshotRestore restore)
    {
        await actor.SetResolvedSnapshotAsync(restore);
        return GetLastSnapshotRestoreSafeWindowThreshold(actor);
    }

    private static async Task<(SerializableMultiProjectionStateEnvelope Envelope, CappedBlobAccessor Blob, int UncompressedWireBytes)>
        CreateSmallGraphLargeWireFixtureAsync()
    {
        // The real JSON graph restored by StreamPayloadProjector is just one short value. The ignored JSON property is
        // deliberately 25 MiB of deterministic high-entropy ASCII, so the wire is large without retaining a matching
        // source object graph. It is the controlled AC1 discriminant, not a no-OOM claim.
        var rawJson = CreateSmallGraphLargeWireJson();
        var gzip = GzipCompression.Compress(rawJson);
        Assert.InRange(gzip.Length, 16 * 1024 * 1024, 32 * 1024 * 1024);

        var blob = new CappedBlobAccessor();
        var key = await blob.WriteAsync(new MemoryStream(gzip, writable: false), StreamPayloadProjector.MultiProjectorName);
        return (
            new SerializableMultiProjectionStateEnvelope(
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
                    Version: 41,
                    IsCatchedUp: true,
                    IsSafeState: true,
                    PayloadLength: gzip.Length,
                    OriginalSizeBytes: rawJson.Length,
                    CompressedSizeBytes: gzip.Length)),
            blob,
            rawJson.Length);
    }

    private static byte[] CreateSmallGraphLargeWireJson()
    {
        const int totalLength = 25 * 1024 * 1024;
        var prefix = Encoding.UTF8.GetBytes("{\"values\":[\"small-graph\"],\"ignoredLargeWireValue\":\"");
        var suffix = Encoding.UTF8.GetBytes("\"}");
        var json = new byte[totalLength];
        prefix.CopyTo(json, 0);
        suffix.CopyTo(json, json.Length - suffix.Length);

        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        uint random = 0x9e37_79b9;
        for (var index = prefix.Length; index < json.Length - suffix.Length; index++)
        {
            random ^= random << 13;
            random ^= random >> 17;
            random ^= random << 5;
            json[index] = (byte)alphabet[(int)(random & 63)];
        }

        return json;
    }

    private static void AssertRecordedThresholdsExactly(
        IReadOnlyList<string> thresholds,
        string actorSelectedThreshold,
        int expectedCount,
        string path)
    {
        Assert.False(string.IsNullOrWhiteSpace(actorSelectedThreshold));
        if (thresholds.Count != expectedCount)
        {
            throw new Xunit.Sdk.XunitException(
                $"{path} must deserialize exactly {expectedCount} times; observed {thresholds.Count}.");
        }

        Assert.All(thresholds, threshold =>
        {
            Assert.False(string.IsNullOrWhiteSpace(threshold));
            Assert.Equal(actorSelectedThreshold, threshold);
        });
    }

    private static void AssertCustomThresholdsExactly(
        MatrixRegistryKind registry,
        string actorSelectedThreshold,
        int expectedCount,
        string path)
    {
        if (registry != MatrixRegistryKind.Custom)
        {
            return;
        }

        AssertRecordedThresholdsExactly(
            MatrixCustomPayloadProjector.DrainRecordedSafeThresholds(),
            actorSelectedThreshold,
            expectedCount,
            path);
    }

    private static MatrixRegistrySetup CreateMatrixRegistry(MatrixRegistryKind registryKind)
    {
        switch (registryKind)
        {
            case MatrixRegistryKind.Reflection:
            {
                var registry = new SimpleMultiProjectorTypes();
                registry.RegisterProjector<MatrixPayloadProjector>();
                return new MatrixRegistrySetup(
                    new ThresholdRecordingRegistry(registry),
                    MatrixPayloadProjector.MultiProjectorName,
                    typeof(MatrixPayloadProjector).FullName!);
            }
            case MatrixRegistryKind.Aot:
            {
                var registry = new AotMultiProjectorTypes();
                registry.RegisterProjector<MatrixPayloadProjector>(MatrixJsonContext.Default.MatrixPayloadProjector);
                return new MatrixRegistrySetup(
                    new ThresholdRecordingRegistry(registry),
                    MatrixPayloadProjector.MultiProjectorName,
                    typeof(MatrixPayloadProjector).FullName!);
            }
            case MatrixRegistryKind.Custom:
            {
                var registry = new SimpleMultiProjectorTypes();
                Assert.True(registry.RegisterProjectorWithCustomSerialization<MatrixCustomPayloadProjector>().IsSuccess);
                return new MatrixRegistrySetup(
                    new ThresholdRecordingRegistry(registry),
                    MatrixCustomPayloadProjector.MultiProjectorName,
                    typeof(MatrixCustomPayloadProjector).FullName!);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(registryKind), registryKind, null);
        }
    }

    private static byte[] CreateMatrixPayloadBytes(
        MatrixCell cell,
        DcbDomainTypes domain,
        string expectedValue)
    {
        var payload = cell.Registry == MatrixRegistryKind.Custom
            ? (IMultiProjectionPayload)new MatrixCustomPayloadProjector(expectedValue)
            : new MatrixPayloadProjector(expectedValue);
        // AOT uses its generated metadata (and therefore its generated wire-name policy), not reflection's domain
        // options. Serializing this cell with the same metadata makes the matrix exercise the real AOT byte and stream
        // readers instead of treating a casing mismatch as an unrelated restore difference.
        var raw = cell.Registry == MatrixRegistryKind.Aot
            ? JsonSerializer.SerializeToUtf8Bytes(
                Assert.IsType<MatrixPayloadProjector>(payload),
                MatrixJsonContext.Default.MatrixPayloadProjector)
            : JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType(), domain.JsonSerializerOptions);
        return cell.PayloadFormat == MatrixPayloadFormat.GzipJson ? GzipCompression.Compress(raw) : raw;
    }

    private static SerializableMultiProjectionState CreateMatrixState(
        MatrixCell cell,
        byte[] payloadBytes,
        string projectorName,
        string payloadType,
        string lastSortableUniqueId,
        Guid lastEventId,
        int version)
    {
        if (cell.WireVersion == MatrixWireVersion.V9)
        {
            Assert.Equal(MatrixPayloadFormat.RawLegacyJson, cell.PayloadFormat);
        }

        return new SerializableMultiProjectionState(
            payloadJson: cell.WireVersion == MatrixWireVersion.V9 ? Encoding.UTF8.GetString(payloadBytes) : null,
            payloadBase64: cell.WireVersion == MatrixWireVersion.V10 ? Convert.ToBase64String(payloadBytes) : null,
            payloadType,
            projectorName,
            MatrixProjectorVersion,
            lastSortableUniqueId,
            lastEventId,
            version,
            isCatchedUp: false,
            isSafeState: true,
            originalSizeBytes: payloadBytes.Length,
            compressedSizeBytes: payloadBytes.Length);
    }

    private static string GetMatrixPayloadValue(IMultiProjectionPayload payload) => payload switch
    {
        MatrixPayloadProjector reflectionOrAot => reflectionOrAot.Value,
        MatrixCustomPayloadProjector custom => custom.Value,
        _ => throw new Xunit.Sdk.XunitException($"Unexpected matrix payload type '{payload.GetType().FullName}'.")
    };

    private static void AssertStreamDeserializerSurface()
    {
        var type = typeof(StreamSnapshotPayloadDeserializer);
        Assert.True(type.IsPublic);
        Assert.True(type.IsAbstract && type.IsSealed);
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.Equal(2, methods.Length);
        var reflection = Assert.Single(methods, method => !method.IsGenericMethodDefinition);
        Assert.Equal("DeserializeJsonAsync", reflection.Name);
        Assert.Equal(typeof(Task<object>), reflection.ReturnType);
        Assert.Equal(
            [typeof(Stream), typeof(Type), typeof(JsonSerializerOptions), typeof(CancellationToken)],
            reflection.GetParameters().Select(parameter => parameter.ParameterType));
        var aot = Assert.Single(methods, method => method.IsGenericMethodDefinition);
        Assert.Equal("DeserializeJsonAsync", aot.Name);
        Assert.Single(aot.GetGenericArguments());
        Assert.Equal(typeof(Task<>), aot.ReturnType.GetGenericTypeDefinition());
        Assert.Equal(
            [typeof(Stream), typeof(JsonTypeInfo<>), typeof(CancellationToken)],
            aot.GetParameters().Select(parameter =>
                parameter.ParameterType.IsGenericType
                    ? parameter.ParameterType.GetGenericTypeDefinition()
                    : parameter.ParameterType));
    }

    private static void AssertStreamingRegistrySurface(Type registryType)
    {
        Assert.Contains(typeof(IStreamingMultiProjectorTypes), registryType.GetInterfaces());
        var methods = registryType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        AssertMethod(methods, nameof(IStreamingMultiProjectorTypes.SupportsStreamDeserialization), typeof(bool), typeof(string));
        AssertMethod(
            methods,
            nameof(IStreamingMultiProjectorTypes.DeserializeFromStreamAsync),
            typeof(Task<ResultBox<IMultiProjectionPayload>>),
            typeof(string), typeof(DcbDomainTypes), typeof(string), typeof(Stream), typeof(CancellationToken));
    }

    private static IReadOnlyList<MethodBase> GetSupportedStreamRestorePathRoots()
    {
        var teeType = GetSnapshotRestoreTeeReadStreamType();
        var streamingSupportType = GetStreamingMultiProjectorTypesSupportType();
        return
        [
            typeof(GeneralMultiProjectionActor).GetMethod(
                "DeserializeStreamingPayloadPairAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!,
            typeof(SnapshotEnvelopeResolver).GetMethod(
                nameof(SnapshotEnvelopeResolver.ResolveForRestoreAsync),
                BindingFlags.Static | BindingFlags.Public)!,
            GetStreamingRegistryDeserializeMethod(typeof(AotMultiProjectorTypes)),
            GetStreamingRegistryDeserializeMethod(typeof(SimpleMultiProjectorTypes)),
            GetStreamingRegistryDeserializeMethod(typeof(GeneralMultiProjectionActor).Assembly.GetType(
                "Sekiban.Dcb.Domains.SimpleMultiProjectorTypes",
                throwOnError: true)!),
            .. GetConcreteMethodsIncludingNestedTypes(teeType),
            .. GetConcreteMethodsIncludingNestedTypes(typeof(StreamSnapshotPayloadDeserializer)),
            .. GetConcreteMethodsIncludingNestedTypes(streamingSupportType)
        ];
    }

    private static Type GetSnapshotRestoreTeeReadStreamType() =>
        typeof(SnapshotEnvelopeResolver).Assembly.GetType(
            "Sekiban.Dcb.Snapshots.SnapshotRestoreTeeReadStream",
            throwOnError: true)!;

    private static Type GetPrefixBufferedStreamType() =>
        typeof(StreamSnapshotPayloadDeserializer).GetNestedType(
            "PrefixBufferedStream",
            BindingFlags.NonPublic)
        ?? throw new Xunit.Sdk.XunitException("StreamSnapshotPayloadDeserializer.PrefixBufferedStream was not found.");

    private static Type GetStreamingMultiProjectorTypesSupportType() =>
        typeof(StreamSnapshotPayloadDeserializer).Assembly.GetType(
            "Sekiban.Dcb.MultiProjections.StreamingMultiProjectorTypesSupport",
            throwOnError: true)!;

    private static MethodInfo GetStreamingRegistryDeserializeMethod(Type registryType) =>
        Assert.Single(
            registryType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
            method => method.Name == nameof(IStreamingMultiProjectorTypes.DeserializeFromStreamAsync));

    private static IEnumerable<MethodBase> GetConcreteMethodsIncludingNestedTypes(Type type)
    {
        foreach (var constructor in type.GetConstructors(
                     BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            yield return constructor;
        }

        if (type.TypeInitializer is { } typeInitializer)
        {
            yield return typeInitializer;
        }

        foreach (var method in type.GetMethods(
                     BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                     BindingFlags.DeclaredOnly)
                 .Where(method => !method.IsAbstract))
        {
            yield return method;
        }

        foreach (var nestedType in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            foreach (var nestedMethod in GetConcreteMethodsIncludingNestedTypes(nestedType))
            {
                yield return nestedMethod;
            }
        }
    }

    private static void AssertNoWholePayloadAggregationReferences(IEnumerable<MethodBase> roots)
    {
        var forbidden = FindWholePayloadAggregationReferences(roots)
            .Select(DescribeMember)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.Empty(forbidden);
    }

    private static IReadOnlyList<MemberInfo> FindWholePayloadAggregationReferences(IEnumerable<MethodBase> roots) =>
        EnumerateTransitiveReferencedMembers(roots)
            .Where(IsWholePayloadAggregationReference)
            .ToArray();

    private static IEnumerable<MemberInfo> EnumerateTransitiveReferencedMembers(IEnumerable<MethodBase> roots)
    {
        var productAssemblies = new HashSet<Assembly>(roots
            .Select(root => root.DeclaringType?.Assembly)
            .OfType<Assembly>())
        {
            typeof(SnapshotEnvelopeResolver).Assembly,
            typeof(StreamSnapshotPayloadDeserializer).Assembly
        };
        var pending = new Stack<MethodBase>(roots.Reverse());
        var visited = new HashSet<MethodBase>();

        while (pending.TryPop(out var candidate))
        {
            var implementation = GetImplementationMethod(candidate);
            if (!visited.Add(implementation))
            {
                continue;
            }

            foreach (var member in EnumerateDirectReferencedMembers(implementation))
            {
                yield return member;
                if (member is MethodBase called &&
                    called.DeclaringType is not null &&
                    productAssemblies.Contains(called.DeclaringType.Assembly))
                {
                    pending.Push(called);
                }
            }
        }
    }

    private static MethodBase GetImplementationMethod(MethodBase method)
    {
        if (method is not MethodInfo asyncMethod ||
            asyncMethod.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType is not { } stateMachine)
        {
            return method;
        }

        return stateMachine.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new Xunit.Sdk.XunitException($"{stateMachine.FullName}.MoveNext was not found.");
    }

    private static IEnumerable<MemberInfo> EnumerateDirectReferencedMembers(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
        {
            yield break;
        }

        var genericTypeArguments = method.DeclaringType?.IsGenericType == true
            ? method.DeclaringType.GetGenericArguments()
            : null;
        var genericMethodArguments = method is MethodInfo methodInfo && methodInfo.IsGenericMethod
            ? methodInfo.GetGenericArguments()
            : null;

        for (var offset = 0; offset < il.Length;)
        {
            var opCode = ReadOpCode(il, ref offset);
            if (opCode.OperandType is OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineTok or OperandType.InlineType)
            {
                var token = BitConverter.ToInt32(il, offset);
                offset += sizeof(int);
                MemberInfo? member = null;
                try
                {
                    member = method.Module.ResolveMember(token, genericTypeArguments, genericMethodArguments);
                }
                catch (Exception exception) when (exception is ArgumentException or BadImageFormatException)
                {
                    // A generic method specification can be unresolved while still having no relevant aggregation call.
                }

                if (member is not null)
                {
                    yield return member;
                }

                continue;
            }

            offset += OperandSize(opCode.OperandType, il, offset);
        }
    }

    private static bool IsWholePayloadAggregationReference(MemberInfo member)
    {
        var declaringType = member.DeclaringType;
        if (declaringType is null)
        {
            return false;
        }

        return
            (declaringType == typeof(StreamReadHelper) && member.Name == "ReadAllBytesAsync") ||
            (declaringType == typeof(SerializableMultiProjectionState) && member.Name == "GetPayloadBytes") ||
            (declaringType == typeof(MemoryStream) && member.Name is ".ctor" or "ToArray" or "GetBuffer") ||
            (declaringType == typeof(File) &&
             (member.Name.StartsWith("ReadAllBytes", StringComparison.Ordinal) ||
              member.Name.StartsWith("ReadAllText", StringComparison.Ordinal))) ||
            (declaringType.IsGenericType &&
             declaringType.GetGenericTypeDefinition() == typeof(System.Buffers.ArrayBufferWriter<>) &&
             member.Name == ".ctor") ||
            (declaringType == typeof(StringBuilder) && member.Name == nameof(StringBuilder.ToString)) ||
            (typeof(TextReader).IsAssignableFrom(declaringType) &&
             member.Name is "ReadToEnd" or "ReadToEndAsync") ||
            (typeof(BinaryReader).IsAssignableFrom(declaringType) &&
             member.Name == nameof(BinaryReader.ReadBytes));
    }

    private static string DescribeMember(MemberInfo member) =>
        $"{member.DeclaringType?.FullName ?? "<unknown>"}.{member.Name}";

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<byte[]> AggregationVerifierAsyncRoot(SerializableMultiProjectionState state)
    {
        await Task.Yield();
        _ = AggregationVerifierHiddenMemoryStreamHelper();
        _ = AggregationVerifierHiddenReadAllBytesHelperAsync();
        return AggregationVerifierHiddenPayloadBytesHelper(state);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static byte[] AggregationVerifierHiddenMemoryStreamHelper()
    {
        using var aggregate = new MemoryStream();
        return aggregate.ToArray();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<byte[]> AggregationVerifierHiddenReadAllBytesHelperAsync()
    {
        await Task.Yield();
        // This helper is scanned, never executed. Its only purpose is to prove that a ReadAllBytes* mutation hidden
        // behind an async helper cannot evade the recursive production-path guard.
        return await File.ReadAllBytesAsync(Path.Combine(Path.GetTempPath(), "aggregation-verifier-never-read"));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static byte[] AggregationVerifierHiddenPayloadBytesHelper(SerializableMultiProjectionState state) =>
        state.GetPayloadBytes();

    private static OpCode ReadOpCode(byte[] il, ref int offset)
    {
        short value = il[offset++];
        if (value == 0xfe)
        {
            value = (short)(0xfe00 | il[offset++]);
        }

        return IlOpCodes[value];
    }

    private static int OperandSize(OperandType operandType, byte[] il, int offset) => operandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.ShortInlineR => 4,
        OperandType.InlineVar => 2,
        OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI or OperandType.InlineMethod or
            OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType => 4,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => sizeof(int) + sizeof(int) * BitConverter.ToInt32(il, offset),
        _ => throw new Xunit.Sdk.XunitException($"Unsupported IL operand type '{operandType}'.")
    };

    private static readonly IReadOnlyDictionary<short, OpCode> IlOpCodes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opCode => opCode.Value);

    private const string MatrixProjectorVersion = "matrix-v1";

    public sealed record MatrixPayloadProjector(string Value) : IMultiProjector<MatrixPayloadProjector>
    {
        public MatrixPayloadProjector() : this(string.Empty) { }
        public static string MultiProjectorName => "streaming-restore-matrix";
        public static string MultiProjectorVersion => MatrixProjectorVersion;
        public static MatrixPayloadProjector GenerateInitialPayload() => new(string.Empty);

        public static ResultBox<MatrixPayloadProjector> Project(
            MatrixPayloadProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) => ResultBox.FromValue(payload);
    }

    public sealed record MatrixCustomPayloadProjector(string Value) :
        ICoreMultiProjectorWithCustomSerialization<MatrixCustomPayloadProjector>,
        ICoreMultiProjectorWithStreamDeserialization
    {
        private static readonly ConcurrentQueue<string> RecordedSafeThresholds = new();

        public MatrixCustomPayloadProjector() : this(string.Empty) { }
        public static string MultiProjectorName => "streaming-restore-matrix-custom";
        public static string MultiProjectorVersion => MatrixProjectorVersion;
        public static MatrixCustomPayloadProjector GenerateInitialPayload() => new(string.Empty);

        public static ResultBox<MatrixCustomPayloadProjector> Project(
            MatrixCustomPayloadProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) => ResultBox.FromValue(payload);

        public static SerializationResult Serialize(
            DcbDomainTypes domainTypes,
            string safeWindowThreshold,
            MatrixCustomPayloadProjector payload)
        {
            var raw = JsonSerializer.SerializeToUtf8Bytes(payload, domainTypes.JsonSerializerOptions);
            var gzip = GzipCompression.Compress(raw);
            return new SerializationResult(gzip, raw.Length, gzip.Length);
        }

        public static MatrixCustomPayloadProjector Deserialize(
            DcbDomainTypes domainTypes,
            string safeWindowThreshold,
            ReadOnlySpan<byte> data)
        {
            RecordedSafeThresholds.Enqueue(safeWindowThreshold);
            var raw = data.Length >= 2 && data[0] == 0x1f && data[1] == 0x8b
                ? GzipCompression.Decompress(data)
                : data.ToArray();
            return JsonSerializer.Deserialize<MatrixCustomPayloadProjector>(raw, domainTypes.JsonSerializerOptions)
                ?? throw new InvalidDataException("matrix custom payload was empty");
        }

        public async Task<IMultiProjectionPayload> DeserializeFromStreamAsync(
            DcbDomainTypes domainTypes,
            string safeWindowThreshold,
            Stream source,
            CancellationToken cancellationToken = default)
        {
            RecordedSafeThresholds.Enqueue(safeWindowThreshold);
            return (await StreamSnapshotPayloadDeserializer.DeserializeJsonAsync(
                    source,
                    typeof(MatrixCustomPayloadProjector),
                    domainTypes.JsonSerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false)) as MatrixCustomPayloadProjector
                ?? throw new InvalidDataException("matrix custom payload was empty");
        }

        internal static void ResetRecordedSafeThresholds()
        {
            while (RecordedSafeThresholds.TryDequeue(out _))
            {
            }
        }

        internal static IReadOnlyList<string> DrainRecordedSafeThresholds()
        {
            var thresholds = new List<string>();
            while (RecordedSafeThresholds.TryDequeue(out var threshold))
            {
                thresholds.Add(threshold);
            }

            return thresholds;
        }
    }

    [JsonSerializable(typeof(MatrixPayloadProjector))]
    private sealed partial class MatrixJsonContext : JsonSerializerContext;

    private sealed class ThresholdRecordingRegistry(ICoreMultiProjectorTypes inner) : DelegatingRegistry(inner), IStreamingMultiProjectorTypes
    {
        private readonly IStreamingMultiProjectorTypes _streaming = Assert.IsAssignableFrom<IStreamingMultiProjectorTypes>(inner);

        public List<string> StreamSafeThresholds { get; } = [];
        public List<string> BufferedSafeThresholds { get; } = [];
        public int StreamDeserializeCalls { get; private set; }

        public override ResultBox<IMultiProjectionPayload> Deserialize(
            string name,
            DcbDomainTypes domain,
            string threshold,
            byte[] data)
        {
            BufferedSafeThresholds.Add(threshold);
            return base.Deserialize(name, domain, threshold, data);
        }

        public bool SupportsStreamDeserialization(string projectorName) =>
            _streaming.SupportsStreamDeserialization(projectorName);

        public async Task<ResultBox<IMultiProjectionPayload>> DeserializeFromStreamAsync(
            string projectorName,
            DcbDomainTypes domainTypes,
            string safeWindowThreshold,
            Stream source,
            CancellationToken cancellationToken = default)
        {
            StreamDeserializeCalls++;
            StreamSafeThresholds.Add(safeWindowThreshold);
            return await _streaming.DeserializeFromStreamAsync(
                    projectorName,
                    domainTypes,
                    safeWindowThreshold,
                    source,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private sealed record MatrixRegistrySetup(
        ThresholdRecordingRegistry Registry,
        string ProjectorName,
        string PayloadType)
    {
        public IReadOnlyList<string> StreamSafeThresholds => Registry.StreamSafeThresholds;
        public IReadOnlyList<string> BufferedSafeThresholds => Registry.BufferedSafeThresholds;
        public int StreamDeserializeCalls => Registry.StreamDeserializeCalls;
    }

    public sealed record MatrixCell(
        MatrixRegistryKind Registry,
        MatrixPayloadFormat PayloadFormat,
        MatrixWireVersion WireVersion,
        MatrixDelivery Delivery);

    public enum MatrixRegistryKind { Reflection, Aot, Custom }
    public enum MatrixPayloadFormat { RawLegacyJson, GzipJson }
    public enum MatrixWireVersion { V9, V10 }
    public enum MatrixDelivery { Offloaded, Inline }

    private sealed record RestoreRollbackSnapshot(
        object StateAccessor,
        Guid LastEventId,
        string LastSortableUniqueId,
        int Version,
        bool IsCatchedUp);

    private static RestoreRollbackSnapshot CaptureRestoreRollbackSnapshot(GeneralMultiProjectionActor actor) => new(
        ReadActorField<object>(actor, "_singleStateAccessor"),
        ReadActorField<Guid>(actor, "_unsafeLastEventId"),
        ReadActorField<string>(actor, "_unsafeLastSortableUniqueId"),
        ReadActorField<int>(actor, "_unsafeVersion"),
        ReadActorField<bool>(actor, "_isCatchedUp"));

    private static void AssertRestoreRollbackSnapshotUnchanged(
        GeneralMultiProjectionActor actor,
        RestoreRollbackSnapshot before)
    {
        Assert.Same(before.StateAccessor, ReadActorField<object>(actor, "_singleStateAccessor"));
        Assert.Equal(before.LastEventId, ReadActorField<Guid>(actor, "_unsafeLastEventId"));
        Assert.Equal(before.LastSortableUniqueId, ReadActorField<string>(actor, "_unsafeLastSortableUniqueId"));
        Assert.Equal(before.Version, ReadActorField<int>(actor, "_unsafeVersion"));
        Assert.Equal(before.IsCatchedUp, ReadActorField<bool>(actor, "_isCatchedUp"));
    }

    private static int GetLastStreamingRestoreWholePayloadAggregationCount(GeneralMultiProjectionActor actor) =>
        ReadActorField<int>(actor, "_lastStreamingRestoreWholePayloadAggregationCount");

    private static string GetLastSnapshotRestoreSafeWindowThreshold(GeneralMultiProjectionActor actor) =>
        ReadActorField<string>(actor, "_lastSnapshotRestoreSafeWindowThreshold");

    private static T ReadActorField<T>(GeneralMultiProjectionActor actor, string name) =>
        (T)(typeof(GeneralMultiProjectionActor)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(actor)
            ?? throw new Xunit.Sdk.XunitException($"Actor field '{name}' was not found."));
}
