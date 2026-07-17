namespace Sekiban.Dcb.Domains;

/// <summary>
///     How a stored event payload is bound back to its CLR type, and what happens when it does not fit.
///     The default exists because of issue #1074: a PascalCase payload that plainly held values deserialized into an
///     instance whose every property was null, with no exception and no warning, because the reader's options were
///     camelCase and case-sensitive. A producer-side data bug became an undiagnosable reader-side mystery. The default
///     turns that specific silence into a loud, descriptive failure — and nothing else, so a correct row is unaffected.
/// </summary>
public enum EventPayloadDeserializationPolicy
{
    /// <summary>
    ///     The default. A top-level JSON member that fails to bind AND matches a declared property name except for
    ///     casing throws <see cref="SekibanEventPayloadBindingException" />. Genuinely unknown members are still
    ///     ignored, so a newer writer's additive field does not break an older reader. Nested objects are not
    ///     inspected — the check is top-level only, by contract.
    /// </summary>
    FailOnCaseMismatch = 0,

    /// <summary>
    ///     The pre-G13 behaviour, unchanged: bind case-sensitively and let a mismatched member stay unbound (null).
    ///     An escape hatch for anyone who was relying on the old silence and needs time to migrate. Not recommended:
    ///     it is the behaviour issue #1074 was about.
    /// </summary>
    CompatibleCaseSensitive = 1,

    /// <summary>
    ///     Strict forward-incompatibility: any top-level member that does not bind — a case mismatch OR a genuinely
    ///     unknown field — throws. Use when a payload that does not map exactly should never be accepted silently.
    ///     Rejects additive fields from newer writers, so it is opt-in, never a default.
    /// </summary>
    StrictUnmapped = 2,

    /// <summary>
    ///     Migration only, and a documented risk. A top-level member whose name differs from a declared name only by
    ///     casing is bound to that member anyway (top-level scope only). This lets you read legacy rows written with
    ///     the wrong casing while you rewrite them; it does not fix the data, and it does nothing for nested casing.
    /// </summary>
    CaseInsensitiveLegacy = 3
}
