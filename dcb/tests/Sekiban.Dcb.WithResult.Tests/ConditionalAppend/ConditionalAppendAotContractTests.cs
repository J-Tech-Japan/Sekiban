using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.TestSupport;
using Sekiban.Dcb.Testing;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
namespace Sekiban.Dcb.Tests.ConditionalAppend;

// A genuine source-generated context whose event carries a type-level custom converter on an otherwise-allowlisted
// enum leaf. Source-gen honours the [JsonConverter], so the effective (AOT) metadata for the leaf is the custom
// converter — which the boundary must reject.
[JsonConverter(typeof(AotHostileEnumConverter))]
public enum AotHostileColor { Red, Green }

public sealed record AotHostileEnumEvent(AotHostileColor Color) : IEventPayload;

public sealed class AotHostileEnumConverter : JsonConverter<AotHostileColor>
{
    public override AotHostileColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Enum.Parse<AotHostileColor>(reader.GetString() ?? nameof(AotHostileColor.Red));

    public override void Write(Utf8JsonWriter writer, AotHostileColor value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

[JsonSerializable(typeof(AotHostileEnumEvent))]
internal partial class AotHostileJsonContext : JsonSerializerContext;

/// <summary>
///     SEK-G15 AOT parity + frozen vector. The fingerprint is computed through the production <see cref="AotEventTypes" />
///     path over genuine source-generated metadata (<see cref="FixtureJsonContext" />), pinned to a literal digest so it
///     fails if AOT type identity, effective metadata, canonicalization version, or the payload algorithm drifts. It also
///     proves parity: the SAME logical event via the reflection (<see cref="SimpleEventTypes" />) path with the same
///     (camelCase) naming produces the SAME fingerprint — the contract's parity guarantee.
/// </summary>
public class ConditionalAppendAotContractTests
{
    private static readonly Guid FixedStudentId = new("11111111-2222-3333-4444-555555555555");
    private static FixtureStudentCreated Sample() => new(FixedStudentId, "Alice", 3);

    private static AotEventTypes AotEventTypesUnderTest()
    {
        var aot = new AotEventTypes();
        aot.Register(nameof(FixtureStudentCreated), FixtureJsonContext.Default.FixtureStudentCreated);
        return aot;
    }

    private static SimpleEventTypes ReflectionEventTypesCamelCase()
    {
        var simple = new SimpleEventTypes(
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false });
        simple.RegisterEventType<FixtureStudentCreated>(nameof(FixtureStudentCreated));
        return simple;
    }

    private static string AotFingerprint()
    {
        var aot = AotEventTypesUnderTest();
        var payload = Encoding.UTF8.GetBytes(aot.SerializeEventPayload(Sample()));
        return OperationFingerprint.ComputeCanonical(
            "svc-aot", "key-aot", aot, nameof(FixtureStudentCreated), payload, new[] { "Tag:1" }).GetValue();
    }


    [Fact]
    public void Aot_ComputeCanonical_FrozenDigest()
    {
        Assert.Equal("8030a459822d89e2e8e66f03af2db69a21b618cdeda9fbf505b39b4cdbd87f4e", AotFingerprint());
    }

    [Fact]
    public void Aot_And_Reflection_SameLogicalEvent_SameNaming_ProduceSameFingerprint()
    {
        var aot = AotEventTypesUnderTest();
        var reflection = ReflectionEventTypesCamelCase();

        var aotPayload = Encoding.UTF8.GetBytes(aot.SerializeEventPayload(Sample()));
        var reflectionPayload = Encoding.UTF8.GetBytes(reflection.SerializeEventPayload(Sample()));

        var aotFp = OperationFingerprint.ComputeCanonical(
            "svc", "key", aot, nameof(FixtureStudentCreated), aotPayload, new[] { "Tag:1" }).GetValue();
        var reflectionFp = OperationFingerprint.ComputeCanonical(
            "svc", "key", reflection, nameof(FixtureStudentCreated), reflectionPayload, new[] { "Tag:1" }).GetValue();

        Assert.Equal(aotFp, reflectionFp);
    }

    [Fact]
    public async Task Aot_HostileConverterLeaf_IsRejectedThroughTheProductionConditionalPath_NoDurableWrite()
    {
        // Genuine source-gen metadata: AotHostileEnumEvent's enum leaf is bound by a custom converter. Registered
        // through AotEventTypes and driven through the real conditional store path (AppendIfUniqueAsync -> ComputeCanonical
        // over the AOT metadata graph), the boundary rejects it with the sanitized typed failure and no durable write.
        var aot = new AotEventTypes();
        aot.Register(nameof(AotHostileEnumEvent), AotHostileJsonContext.Default.AotHostileEnumEvent);
        var store = new InMemoryConditionalEventStore(aot);
        var payload = Encoding.UTF8.GetBytes(aot.SerializeEventPayload(new AotHostileEnumEvent(AotHostileColor.Red)));
        var serializable = new SerializableEvent(payload, Common.SortableUniqueId.GenerateNew(), Guid.CreateVersion7(),
            new EventMetadata("c", "c", "u"), new List<string>(), nameof(AotHostileEnumEvent));

        var r = await store.AppendIfUniqueAsync(new ConditionalAppendRequest("k", serializable));

        Assert.False(r.IsSuccess);
        var ex = Assert.IsType<OperationCanonicalizationException>(r.GetException());
        Assert.Null(ex.InnerException);
        Assert.Equal(0, store.WriteCalls);
        Assert.Empty((await store.ReadAllSerializableEventsAsync()).GetValue());
    }

    [Fact]
    public void Aot_SupportedShapeBoundary_AcceptsTheSourceGenObject()
    {
        // The source-gen JsonTypeInfo is an Object graph of primitive leaves, so the boundary admits it (no exception).
        var aot = AotEventTypesUnderTest();
        var payload = Encoding.UTF8.GetBytes(aot.SerializeEventPayload(Sample()));
        var r = OperationFingerprint.ComputeCanonical(
            "svc", "key", aot, nameof(FixtureStudentCreated), payload, new[] { "Tag:1" });
        Assert.True(r.IsSuccess);
    }
}
