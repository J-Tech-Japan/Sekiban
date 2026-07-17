using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.TestSupport;

/// <summary>
///     Deterministic malformed event payloads, in one place, so SEK-G13 and SEK-G14 test the same bytes.
///     Everything here is a hand-written JSON string, never a serializer's output — the whole point is to reproduce
///     what a mis-cased or over-full producer actually wrote, which a correct serializer would never emit. The strings
///     are stable so a test can assert on exact positions if it needs to.
/// </summary>
public static class MalformedEventPayloadFixture
{
    /// <summary>The event type name the fixtures below are written for.</summary>
    public const string EventTypeName = nameof(FixtureStudentCreated);

    /// <summary>
    ///     The canonical, correct payload: camelCase, exactly the members declared. Reading this must always succeed
    ///     and must never trip a casing check — it is the control.
    /// </summary>
    public const string CanonicalCamelCase =
        """{"studentId":"11111111-1111-1111-1111-111111111111","name":"Alice","maxClassCount":5}""";

    /// <summary>
    ///     The issue #1074 payload: PascalCase member names. Every value is present; a camelCase, case-sensitive reader
    ///     binds none of them and returns an all-null instance. This is what must now fail loud by default.
    /// </summary>
    public const string TopLevelPascalCase =
        """{"StudentId":"11111111-1111-1111-1111-111111111111","Name":"Alice","MaxClassCount":5}""";

    /// <summary>
    ///     Exactly one member mis-cased; the rest correct. Proves the check fires on a single offending member, not
    ///     only on a wholesale casing flip.
    /// </summary>
    public const string SingleMemberPascalCase =
        """{"StudentId":"11111111-1111-1111-1111-111111111111","name":"Alice","maxClassCount":5}""";

    /// <summary>
    ///     Correct top-level casing, but a NESTED object whose key is mis-cased. The contract is top-level only, so
    ///     this must NOT trip the check — the nested casing is out of scope and stays STJ's business.
    /// </summary>
    public const string NestedMemberPascalCase =
        """{"studentId":"11111111-1111-1111-1111-111111111111","name":"Alice","maxClassCount":5,"detail":{"Campus":"north"}}""";

    /// <summary>
    ///     Correct payload plus an additive member a newer writer might add. Default and legacy policies must ignore it
    ///     (forward compatibility); StrictUnmapped must reject it.
    /// </summary>
    public const string AdditiveUnknownField =
        """{"studentId":"11111111-1111-1111-1111-111111111111","name":"Alice","maxClassCount":5,"nickname":"Al"}""";

    /// <summary>A member matching nothing declared, not even by casing — a plain typo. Ignored except under StrictUnmapped.</summary>
    public const string UnrelatedUnknownField =
        """{"studentId":"11111111-1111-1111-1111-111111111111","name":"Alice","maxClassCount":5,"totallyUnknown":"x"}""";

    /// <summary>A payload missing a member. Only matters for types that declare it required.</summary>
    public const string MissingMember =
        """{"studentId":"11111111-1111-1111-1111-111111111111","maxClassCount":5}""";

    /// <summary>The canonical payload for the required-member fixture type.</summary>
    public const string RequiredMemberCanonical =
        """{"studentId":"11111111-1111-1111-1111-111111111111","name":"Alice"}""";
}

/// <summary>A minimal event payload with the members the fixtures above are written against.</summary>
public sealed record FixtureStudentCreated(Guid StudentId, string Name, int MaxClassCount) : IEventPayload;

/// <summary>An event payload with a member declared <c>required</c>, to exercise metadata-declared required enforcement.</summary>
public sealed record FixtureStudentWithRequired : IEventPayload
{
    public Guid StudentId { get; init; }
    public required string Name { get; init; }
}

/// <summary>An event type that declares two JSON names equal under case folding but different with it — deliberately ambiguous.</summary>
public sealed record FixtureAmbiguousCasing : IEventPayload
{
    public string Value { get; init; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("VALUE")]
    public string ValueUpper { get; init; } = string.Empty;
}
