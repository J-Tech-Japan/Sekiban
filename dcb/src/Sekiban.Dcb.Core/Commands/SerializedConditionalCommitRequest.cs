using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Commands;

/// <summary>
///     NEW, versioned serialized-commit request for the WASM boundary's conditional (unique-key) single-event append.
///     Separate from the existing positional <see cref="SerializedCommitRequest" />, which is left untouched. The
///     <see cref="Version" /> field is explicit so the wire contract can evolve without a positional break.
/// </summary>
public record SerializedConditionalCommitRequest(
    int Version,
    SerializableEventCandidate EventCandidate,
    string IdempotencyKey)
{
    /// <summary>The current wire version of this request shape.</summary>
    public const int CurrentVersion = 1;
}
