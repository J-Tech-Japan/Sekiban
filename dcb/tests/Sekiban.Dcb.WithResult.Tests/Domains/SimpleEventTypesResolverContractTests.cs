using Sekiban.Dcb.Domains;
using Sekiban.Dcb.TestSupport;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Xunit;
namespace Sekiban.Dcb.WithResult.Tests.Domains;

/// <summary>
///     The reflection preflight must check the names the deserializer will actually bind — including the names a
///     caller's resolver MODIFIER produced, not the convention-derived names. A preflight that guessed the names from
///     the naming policy alone would reject valid payloads under StrictUnmapped, and — worse — miss the exact case
///     mismatch this slice exists to catch when the mismatched member was one a modifier renamed. These tests exist
///     because the original matrix only ever built camelCase options with no custom resolver, and so could not have
///     seen that divergence.
/// </summary>
public class SimpleEventTypesResolverContractTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>Options whose resolver renames <c>FixtureStudentCreated.Name</c>'s JSON name to <c>displayName</c>.</summary>
    private static JsonSerializerOptions RenamingOptions() =>
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers =
                {
                    typeInfo =>
                    {
                        if (typeInfo.Type != typeof(FixtureStudentCreated))
                        {
                            return;
                        }

                        foreach (var property in typeInfo.Properties)
                        {
                            if (property.Name == "name")
                            {
                                property.Name = "displayName";
                            }
                        }
                    }
                }
            }
        };

    private static SimpleEventTypes Build(JsonSerializerOptions options, EventPayloadDeserializationPolicy policy)
    {
        var types = new SimpleEventTypes(options, policy);
        types.RegisterEventType<FixtureStudentCreated>(MalformedEventPayloadFixture.EventTypeName);
        return types;
    }

    [Fact]
    public void RenamedMember_BindsUnderItsModifiedName_AndIsNotRejectedAsUnknown()
    {
        // "displayName" is the name the modifier produced, so it is the name a correct payload uses AND the name the
        // preflight must accept — even under StrictUnmapped, which rejects anything unmapped.
        var payload = Build(RenamingOptions(), EventPayloadDeserializationPolicy.StrictUnmapped)
            .DeserializeEventPayload(
                MalformedEventPayloadFixture.EventTypeName,
                $$"""{"studentId":"{{Id}}","displayName":"Alice","maxClassCount":5}""");

        var student = Assert.IsType<FixtureStudentCreated>(payload);
        Assert.Equal("Alice", student.Name);
    }

    [Fact]
    public void CaseMismatchOfARenamedMember_IsCaughtAgainstTheModifiedName()
    {
        // "DisplayName" folds to the modifier's "displayName". A preflight checking the convention name ("name") would
        // see "DisplayName" as unrelated and let it bind to null — the very silent-null this slice must prevent. The
        // preflight must instead recognise it as a case mismatch of "displayName".
        var ex = Assert.Throws<SekibanEventPayloadBindingException>(
            () => Build(RenamingOptions(), EventPayloadDeserializationPolicy.FailOnCaseMismatch)
                .DeserializeEventPayload(
                    MalformedEventPayloadFixture.EventTypeName,
                    $$"""{"studentId":"{{Id}}","DisplayName":"Alice","maxClassCount":5}"""));

        Assert.Equal("DisplayName", ex.OffendingJsonName);
        Assert.Equal("displayName", ex.ExpectedJsonName);
    }

    [Fact]
    public void TheConventionName_OfARenamedMember_IsTreatedAsUnknown_NotAsAMatch()
    {
        // After the rename, "name" is no longer a declared JSON name. Under the default policy it is a genuinely
        // unknown field (it does not fold to "displayName"), so it is ignored — and the payload has no displayName, so
        // Name binds to null. This pins that the preflight is reading modified names, not convention names.
        var payload = Build(RenamingOptions(), EventPayloadDeserializationPolicy.FailOnCaseMismatch)
            .DeserializeEventPayload(
                MalformedEventPayloadFixture.EventTypeName,
                $$"""{"studentId":"{{Id}}","name":"Alice","maxClassCount":5}""");

        var student = Assert.IsType<FixtureStudentCreated>(payload);
        Assert.Null(student.Name);
    }

    [Fact]
    public void TheConventionName_OfARenamedMember_IsRejected_UnderStrictUnmapped()
    {
        var ex = Assert.Throws<SekibanEventPayloadBindingException>(
            () => Build(RenamingOptions(), EventPayloadDeserializationPolicy.StrictUnmapped)
                .DeserializeEventPayload(
                    MalformedEventPayloadFixture.EventTypeName,
                    $$"""{"studentId":"{{Id}}","name":"Alice","maxClassCount":5}"""));

        Assert.Equal("name", ex.OffendingJsonName);
    }

    [Fact]
    public void OptionsWithACustomResolverThatCannotDescribeTheType_FailDeterministically_NotSilently()
    {
        // A resolver that describes nothing. The real deserialize would throw; the preflight must not silently revert
        // to a pre-G13 unchecked bind. Under a metadata-requiring policy the failure is loud and policy-visible.
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = new EmptyResolver()
        };

        var ex = Assert.Throws<SekibanEventPayloadBindingException>(
            () => Build(options, EventPayloadDeserializationPolicy.FailOnCaseMismatch)
                .DeserializeEventPayload(
                    MalformedEventPayloadFixture.EventTypeName,
                    MalformedEventPayloadFixture.CanonicalCamelCase));

        Assert.Contains("no JSON metadata could be resolved", ex.Message);
    }

    [Fact]
    public void OptionsWithNoResolverYet_StillCheck_MirroringWhatTheDeserializeWillDo()
    {
        // A fresh options with no resolver: GetTypeInfo would throw, but the real deserialize auto-attaches the default
        // reflection resolver. The preflight mirrors exactly that on a copy, so the check still runs — this is the
        // regression that no-op'd the whole Simple pipeline in the first cut.
        var freshOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        var ex = Assert.Throws<SekibanEventPayloadBindingException>(
            () => Build(freshOptions, EventPayloadDeserializationPolicy.FailOnCaseMismatch)
                .DeserializeEventPayload(
                    MalformedEventPayloadFixture.EventTypeName,
                    MalformedEventPayloadFixture.TopLevelPascalCase));

        Assert.Equal("StudentId", ex.OffendingJsonName);
    }

    [Fact]
    public void CompatibleCaseSensitive_StillBinds_EvenWithoutResolvableMetadata()
    {
        // The escape hatch is the one policy allowed to bind without a check. A resolver that cannot describe the type
        // therefore does not fail here — it deserializes as pre-G13 would (and STJ itself decides the outcome).
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = new EmptyResolver()
        };

        Assert.Throws<NotSupportedException>(
            () => Build(options, EventPayloadDeserializationPolicy.CompatibleCaseSensitive)
                .DeserializeEventPayload(
                    MalformedEventPayloadFixture.EventTypeName,
                    MalformedEventPayloadFixture.CanonicalCamelCase));
    }

    /// <summary>A resolver that describes no type — to exercise the "no effective metadata" path deterministically.</summary>
    private sealed class EmptyResolver : IJsonTypeInfoResolver
    {
        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) => null;
    }
}
