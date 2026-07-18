using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.TestSupport;
using System.Text;
using System.Text.Json;
using Xunit;
namespace Sekiban.Dcb.Tests.ConditionalAppend;

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
