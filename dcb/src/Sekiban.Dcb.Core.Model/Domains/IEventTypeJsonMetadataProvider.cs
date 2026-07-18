using System.Text.Json.Serialization.Metadata;
namespace Sekiban.Dcb.Domains;

/// <summary>
///     OPTIONAL, additive capability an <see cref="IEventTypes" /> may implement to expose the EFFECTIVE
///     <see cref="JsonTypeInfo" /> that its (de)serialize actually binds with — reflection (Simple) or source-gen (AOT)
///     alike. Feature-detected with <c>is</c>; never a member of <see cref="IEventTypes" />, so existing implementors
///     compile untouched.
///     The conditional-append canonical fingerprint uses it to prove a payload's shape is deterministically
///     canonicalizable BEFORE hashing. An <see cref="IEventTypes" /> that does NOT implement this cannot prove any shape,
///     so conditional append fails closed for it — which is the conservative, correct default.
/// </summary>
public interface IEventTypeJsonMetadataProvider
{
    /// <summary>
    ///     The effective <see cref="JsonTypeInfo" /> for the registered event type, or null when none can be resolved
    ///     (e.g. a resolver that cannot describe the type). The returned metadata is read-only to the caller and must not
    ///     be mutated.
    /// </summary>
    JsonTypeInfo? GetEffectiveTypeInfo(string eventTypeName);
}
