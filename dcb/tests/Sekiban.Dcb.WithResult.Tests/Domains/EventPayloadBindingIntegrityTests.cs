using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.TestSupport;
using System.Text.Json;
using Xunit;
namespace Sekiban.Dcb.WithResult.Tests.Domains;

/// <summary>
///     The behaviour issue #1074 asked for, pinned identically across BOTH pipelines.
///     Every case runs twice — once against <see cref="SimpleEventTypes" /> (reflection) and once against
///     <see cref="AotEventTypes" /> (source-generated <c>JsonTypeInfo</c>) — because the failure they exist to prevent
///     is silent, and a silent failure that only one pipeline catches is a silent failure. The two are driven from one
///     theory so they cannot quietly diverge: if a policy ever behaves differently under source-gen, exactly one row
///     turns red.
/// </summary>
public class EventPayloadBindingIntegrityTests
{
    public enum Pipeline
    {
        Reflection,
        SourceGenerated
    }

    /// <summary>Builds an <see cref="IEventTypes" /> of the requested pipeline, registered for the fixture type, under a policy.</summary>
    private static IEventTypes Build(Pipeline pipeline, EventPayloadDeserializationPolicy policy)
    {
        // camelCase, case-sensitive — the exact options the domain builder injects, and the ones #1074 was written on.
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        if (pipeline == Pipeline.Reflection)
        {
            var simple = new SimpleEventTypes(options, policy);
            simple.RegisterEventType<FixtureStudentCreated>(MalformedEventPayloadFixture.EventTypeName);
            simple.RegisterEventType<FixtureStudentWithRequired>(nameof(FixtureStudentWithRequired));
            simple.RegisterEventType<FixtureAmbiguousCasing>(nameof(FixtureAmbiguousCasing));
            return simple;
        }

        var aot = new AotEventTypes(options, policy);
        var context = new FixtureJsonContext(options);
        aot.Register(
            MalformedEventPayloadFixture.EventTypeName,
            typeof(FixtureStudentCreated),
            context.GetTypeInfo(typeof(FixtureStudentCreated))!);
        aot.Register(
            nameof(FixtureStudentWithRequired),
            typeof(FixtureStudentWithRequired),
            context.GetTypeInfo(typeof(FixtureStudentWithRequired))!);
        aot.Register(
            nameof(FixtureAmbiguousCasing),
            typeof(FixtureAmbiguousCasing),
            context.GetTypeInfo(typeof(FixtureAmbiguousCasing))!);
        return aot;
    }

    private static readonly Pipeline[] Pipelines = [Pipeline.Reflection, Pipeline.SourceGenerated];

    public static TheoryData<Pipeline> BothPipelines()
    {
        var data = new TheoryData<Pipeline>();
        foreach (var p in Pipelines)
        {
            data.Add(p);
        }
        return data;
    }

    // --- the control: correct rows are never touched, under any policy ---

    [Theory]
    [MemberData(nameof(BothPipelines))]
    public void CanonicalCamelCase_Binds_UnderEveryPolicy(Pipeline pipeline)
    {
        foreach (var policy in Enum.GetValues<EventPayloadDeserializationPolicy>())
        {
            var payload = Build(pipeline, policy)
                .DeserializeEventPayload(MalformedEventPayloadFixture.EventTypeName, MalformedEventPayloadFixture.CanonicalCamelCase);

            var student = Assert.IsType<FixtureStudentCreated>(payload);
            Assert.Equal("Alice", student.Name);
            Assert.Equal(5, student.MaxClassCount);
        }
    }

    // --- default: fail loud on top-level case mismatch ---

    [Theory]
    [MemberData(nameof(BothPipelines))]
    public void TopLevelPascalCase_FailsLoud_ByDefault(Pipeline pipeline)
    {
        var types = Build(pipeline, EventPayloadDeserializationPolicy.FailOnCaseMismatch);

        var ex = Assert.Throws<SekibanEventPayloadBindingException>(
            () => types.DeserializeEventPayload(MalformedEventPayloadFixture.EventTypeName, MalformedEventPayloadFixture.TopLevelPascalCase));

        Assert.Equal(MalformedEventPayloadFixture.EventTypeName, ex.EventTypeName);
        Assert.Equal("StudentId", ex.OffendingJsonName);
        Assert.Equal("studentId", ex.ExpectedJsonName);
        Assert.Equal("$.StudentId", ex.PayloadLocation);
        // secret-free: the value "Alice" / the guid must not be in the message
        Assert.DoesNotContain("Alice", ex.Message);
        Assert.DoesNotContain("11111111", ex.Message);
    }

    [Theory]
    [MemberData(nameof(BothPipelines))]
    public void SingleMiscasedMember_FailsLoud_ByDefault(Pipeline pipeline)
    {
        var types = Build(pipeline, EventPayloadDeserializationPolicy.FailOnCaseMismatch);

        var ex = Assert.Throws<SekibanEventPayloadBindingException>(
            () => types.DeserializeEventPayload(MalformedEventPayloadFixture.EventTypeName, MalformedEventPayloadFixture.SingleMemberPascalCase));

        Assert.Equal("StudentId", ex.OffendingJsonName);
    }

    // --- the top-level contract: nested casing is out of scope, and must NOT fire ---

    [Theory]
    [MemberData(nameof(BothPipelines))]
    public void NestedCaseMismatch_DoesNotTrip_TheTopLevelCheck(Pipeline pipeline)
    {
        // The top-level members are all correctly cased; only a nested object's key is Pascal. The contract is
        // top-level only, so this binds without throwing — the nested key simply stays STJ's business.
        var payload = Build(pipeline, EventPayloadDeserializationPolicy.FailOnCaseMismatch)
            .DeserializeEventPayload(MalformedEventPayloadFixture.EventTypeName, MalformedEventPayloadFixture.NestedMemberPascalCase);

        var student = Assert.IsType<FixtureStudentCreated>(payload);
        Assert.Equal("Alice", student.Name);
    }

    // --- forward compatibility vs strictness on genuinely unknown fields ---

    [Theory]
    [MemberData(nameof(BothPipelines))]
    public void AdditiveUnknownField_Ignored_ByDefault(Pipeline pipeline)
    {
        var payload = Build(pipeline, EventPayloadDeserializationPolicy.FailOnCaseMismatch)
            .DeserializeEventPayload(MalformedEventPayloadFixture.EventTypeName, MalformedEventPayloadFixture.AdditiveUnknownField);

        Assert.IsType<FixtureStudentCreated>(payload);
    }

    [Theory]
    [MemberData(nameof(BothPipelines))]
    public void AdditiveUnknownField_Rejected_UnderStrictUnmapped(Pipeline pipeline)
    {
        var types = Build(pipeline, EventPayloadDeserializationPolicy.StrictUnmapped);

        var ex = Assert.Throws<SekibanEventPayloadBindingException>(
            () => types.DeserializeEventPayload(MalformedEventPayloadFixture.EventTypeName, MalformedEventPayloadFixture.AdditiveUnknownField));

        Assert.Equal("nickname", ex.OffendingJsonName);
    }

    [Theory]
    [MemberData(nameof(BothPipelines))]
    public void UnrelatedUnknownField_Ignored_ByDefault(Pipeline pipeline)
    {
        var payload = Build(pipeline, EventPayloadDeserializationPolicy.FailOnCaseMismatch)
            .DeserializeEventPayload(MalformedEventPayloadFixture.EventTypeName, MalformedEventPayloadFixture.UnrelatedUnknownField);

        Assert.IsType<FixtureStudentCreated>(payload);
    }

    // --- the escape hatch and the migration opt-in ---

    [Theory]
    [MemberData(nameof(BothPipelines))]
    public void TopLevelPascalCase_BindsToNulls_UnderCompatibleCaseSensitive(Pipeline pipeline)
    {
        // The pre-G13 behaviour, deliberately preserved: no throw, and the mis-cased values are dropped. This is the
        // silence #1074 was about; the test asserts it still exists for anyone who needs time to migrate.
        var payload = Build(pipeline, EventPayloadDeserializationPolicy.CompatibleCaseSensitive)
            .DeserializeEventPayload(MalformedEventPayloadFixture.EventTypeName, MalformedEventPayloadFixture.TopLevelPascalCase);

        var student = Assert.IsType<FixtureStudentCreated>(payload);
        Assert.Null(student.Name);
        Assert.Equal(0, student.MaxClassCount);
        Assert.Equal(Guid.Empty, student.StudentId);
    }

    [Theory]
    [MemberData(nameof(BothPipelines))]
    public void TopLevelPascalCase_Binds_UnderCaseInsensitiveLegacy(Pipeline pipeline)
    {
        // The migration opt-in: the same PascalCase row now reads its values, top-level only.
        var payload = Build(pipeline, EventPayloadDeserializationPolicy.CaseInsensitiveLegacy)
            .DeserializeEventPayload(MalformedEventPayloadFixture.EventTypeName, MalformedEventPayloadFixture.TopLevelPascalCase);

        var student = Assert.IsType<FixtureStudentCreated>(payload);
        Assert.Equal("Alice", student.Name);
        Assert.Equal(5, student.MaxClassCount);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), student.StudentId);
    }

    // --- ambiguous declared names: deterministic refusal, never a silent pick ---

    [Theory]
    [MemberData(nameof(BothPipelines))]
    public void AmbiguousCaseFoldedDeclaredNames_AreRejected(Pipeline pipeline)
    {
        var types = Build(pipeline, EventPayloadDeserializationPolicy.FailOnCaseMismatch);

        var ex = Assert.Throws<SekibanEventPayloadBindingException>(
            () => types.DeserializeEventPayload(
                nameof(FixtureAmbiguousCasing),
                """{"value":"a","VALUE":"b"}"""));

        Assert.Contains("differ only by casing", ex.Message);
    }

    // --- required members surface through the same descriptive wrapper ---

    [Theory]
    [MemberData(nameof(BothPipelines))]
    public void MissingRequiredMember_FailsLoud(Pipeline pipeline)
    {
        var types = Build(pipeline, EventPayloadDeserializationPolicy.FailOnCaseMismatch);

        var ex = Assert.Throws<SekibanEventPayloadBindingException>(
            () => types.DeserializeEventPayload(
                nameof(FixtureStudentWithRequired),
                """{"studentId":"11111111-1111-1111-1111-111111111111"}"""));

        Assert.Contains("required", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(BothPipelines))]
    public void RequiredMemberPresent_Binds(Pipeline pipeline)
    {
        var payload = Build(pipeline, EventPayloadDeserializationPolicy.FailOnCaseMismatch)
            .DeserializeEventPayload(
                nameof(FixtureStudentWithRequired),
                MalformedEventPayloadFixture.RequiredMemberCanonical);

        var student = Assert.IsType<FixtureStudentWithRequired>(payload);
        Assert.Equal("Alice", student.Name);
    }

    // --- the non-mutation guarantee the AOT pipeline depends on ---

    [Fact]
    public void Preflight_DoesNotMutate_CallerOptions_Or_TypeInfo()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var context = new FixtureJsonContext(options);
        var typeInfo = context.GetTypeInfo(typeof(FixtureStudentCreated))!;

        var caseInsensitiveBefore = options.PropertyNameCaseInsensitive;
        var unmappedBefore = options.UnmappedMemberHandling;

        // Drive every policy over the type; none may write back onto the options or the source-gen metadata.
        foreach (var policy in Enum.GetValues<EventPayloadDeserializationPolicy>())
        {
            try
            {
                EventPayloadBinder.Preflight(
                    MalformedEventPayloadFixture.CanonicalCamelCase,
                    typeInfo,
                    MalformedEventPayloadFixture.EventTypeName,
                    policy);
                EventPayloadBinder.Preflight(
                    MalformedEventPayloadFixture.TopLevelPascalCase,
                    typeInfo,
                    MalformedEventPayloadFixture.EventTypeName,
                    policy);
            }
            catch (SekibanEventPayloadBindingException)
            {
                // expected for FailOnCaseMismatch / StrictUnmapped on the PascalCase row
            }
        }

        Assert.Equal(caseInsensitiveBefore, options.PropertyNameCaseInsensitive);
        Assert.Equal(unmappedBefore, options.UnmappedMemberHandling);
        // the source-gen typeInfo still reads camelCase names, unchanged
        Assert.Contains(typeInfo.Properties, p => p.Name == "studentId");
        Assert.DoesNotContain(typeInfo.Properties, p => p.Name == "StudentId");
    }
}
