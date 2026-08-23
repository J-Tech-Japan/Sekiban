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
        var legacyThresholdWindow = await RestoreInlineAndCaptureThresholdWindowAsync(
            legacyActor,
            legacyState);
        var legacy = await legacyActor.GetStateAsync(canGetUnsafeState: true);
        Assert.True(legacy.IsSuccess);
        AssertRecordedThresholdsWithinWindow(
            byteSetup.BufferedSafeThresholds,
            legacyThresholdWindow,
            "legacy byte path");
        AssertCustomThresholdsWithinWindow(cell.Registry, legacyThresholdWindow, "legacy byte path");

        var restoreSetup = CreateMatrixRegistry(cell.Registry);
        var restoreActor = new GeneralMultiProjectionActor(CreateDomain(restoreSetup.Registry), restoreSetup.ProjectorName);
        MatrixCustomPayloadProjector.ResetRecordedSafeThresholds();
        ThresholdWindow restoreThresholdWindow;

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
            restoreThresholdWindow = await RestoreStreamAndCaptureThresholdWindowAsync(restoreActor, resolved);
            Assert.Equal(2, restoreSetup.StreamDeserializeCalls);
            Assert.Empty(restoreSetup.BufferedSafeThresholds);
            AssertRecordedThresholdsWithinWindow(
                restoreSetup.StreamSafeThresholds,
                restoreThresholdWindow,
                "offloaded stream path");
        }
        else
        {
            restoreThresholdWindow = await RestoreInlineAndCaptureThresholdWindowAsync(restoreActor, legacyState);
            Assert.Equal(0, restoreSetup.StreamDeserializeCalls);
            AssertRecordedThresholdsWithinWindow(
                restoreSetup.BufferedSafeThresholds,
                restoreThresholdWindow,
                "inline compatibility path");
        }

        AssertCustomThresholdsWithinWindow(
            cell.Registry,
            restoreThresholdWindow,
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
    public void Supported_stream_restore_state_machines_have_no_whole_payload_aggregation_api_reference()
    {
        // This is a production structural pin, not read-size telemetry. It kills a mutation that replaces the file tee
        // with MemoryStream/ToArray or reaches StreamReadHelper from the capability-present seam.
        AssertNoWholePayloadAggregationReferences(
            typeof(GeneralMultiProjectionActor).GetMethod(
                "DeserializeStreamingPayloadPairAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!);
        AssertNoWholePayloadAggregationReferences(
            typeof(SnapshotEnvelopeResolver).GetMethod(
                nameof(SnapshotEnvelopeResolver.ResolveForRestoreAsync),
                BindingFlags.Static | BindingFlags.Public)!);
        foreach (var method in typeof(StreamSnapshotPayloadDeserializer).GetMethods(
                     BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            AssertNoWholePayloadAggregationReferences(method);
        }
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

    private static async Task<ThresholdWindow> RestoreInlineAndCaptureThresholdWindowAsync(
        GeneralMultiProjectionActor actor,
        SerializableMultiProjectionState state)
    {
        var lower = actor.PeekCurrentSafeWindowThreshold().Value;
        await actor.SetSnapshotAsync(new SerializableMultiProjectionStateEnvelope(false, state, null));
        return new ThresholdWindow(lower, actor.PeekCurrentSafeWindowThreshold().Value);
    }

    private static async Task<ThresholdWindow> RestoreStreamAndCaptureThresholdWindowAsync(
        GeneralMultiProjectionActor actor,
        ResolvedSnapshotRestore restore)
    {
        var lower = actor.PeekCurrentSafeWindowThreshold().Value;
        await actor.SetResolvedSnapshotAsync(restore);
        return new ThresholdWindow(lower, actor.PeekCurrentSafeWindowThreshold().Value);
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
        var prefix = Encoding.UTF8.GetBytes("{\"Values\":[\"small-graph\"],\"IgnoredLargeWireValue\":\"");
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

    private static void AssertRecordedThresholdsWithinWindow(
        IReadOnlyList<string> thresholds,
        ThresholdWindow window,
        string path)
    {
        Assert.NotEmpty(thresholds);
        Assert.All(thresholds, threshold =>
        {
            Assert.False(string.IsNullOrWhiteSpace(threshold));
            Assert.InRange(
                string.CompareOrdinal(threshold, window.LowerInclusive),
                0,
                int.MaxValue);
            Assert.InRange(
                string.CompareOrdinal(threshold, window.UpperInclusive),
                int.MinValue,
                0);
        });
    }

    private static void AssertCustomThresholdsWithinWindow(
        MatrixRegistryKind registry,
        ThresholdWindow window,
        string path)
    {
        if (registry != MatrixRegistryKind.Custom)
        {
            return;
        }

        AssertRecordedThresholdsWithinWindow(
            MatrixCustomPayloadProjector.DrainRecordedSafeThresholds(),
            window,
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

    private static void AssertNoWholePayloadAggregationReferences(MethodInfo asyncMethod)
    {
        var forbidden = EnumerateReferencedMembers(asyncMethod)
            .Where(member =>
                (member.DeclaringType == typeof(StreamReadHelper) && member.Name == "ReadAllBytesAsync") ||
                (member.DeclaringType == typeof(MemoryStream) && member.Name is ".ctor" or "ToArray") ||
                (member.DeclaringType == typeof(File) && member.Name is "ReadAllBytes" or "ReadAllBytesAsync") ||
                (member.DeclaringType == typeof(SerializableMultiProjectionState) && member.Name == "GetPayloadBytes"))
            .Select(member => $"{member.DeclaringType!.FullName}.{member.Name}")
            .ToArray();
        Assert.Empty(forbidden);
    }

    private static IEnumerable<MemberInfo> EnumerateReferencedMembers(MethodInfo asyncMethod)
    {
        var stateMachine = asyncMethod.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
            ?? throw new Xunit.Sdk.XunitException($"{asyncMethod.Name} is expected to be an async state machine.");
        var moveNext = stateMachine.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new Xunit.Sdk.XunitException($"{stateMachine.FullName}.MoveNext was not found.");
        var il = moveNext.GetMethodBody()?.GetILAsByteArray()
            ?? throw new Xunit.Sdk.XunitException($"{stateMachine.FullName}.MoveNext has no IL body.");
        var genericTypeArguments = stateMachine.IsGenericType ? stateMachine.GetGenericArguments() : null;
        var genericMethodArguments = asyncMethod.IsGenericMethod ? asyncMethod.GetGenericArguments() : null;

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
                    member = moveNext.Module.ResolveMember(token, genericTypeArguments, genericMethodArguments);
                }
                catch (ArgumentException)
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

    private sealed record ThresholdWindow(string LowerInclusive, string UpperInclusive);

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

    private static T ReadActorField<T>(GeneralMultiProjectionActor actor, string name) =>
        (T)(typeof(GeneralMultiProjectionActor)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(actor)
            ?? throw new Xunit.Sdk.XunitException($"Actor field '{name}' was not found."));
}
