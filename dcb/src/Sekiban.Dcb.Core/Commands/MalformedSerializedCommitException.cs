namespace Sekiban.Dcb.Commands;

/// <summary>
///     SEK-G17 typed shape failure for a serialized-commit envelope whose STRUCTURE is invalid: the version discriminator is
///     present but not a JSON integer, appears more than once, or a known-version (V1) / legacy-unversioned payload cannot
///     be bound to its DTO. It replaces the previous null-reference 500 with a clear, typed contract error.
///     <para>
///         Distinct from <see cref="UnsupportedSerializedCommitEnvelopeVersionException" /> (a well-formed but unknown
///         version). A malformed shape is never routed to the executor or store.
///     </para>
/// </summary>
public sealed class MalformedSerializedCommitException : Exception
{
    public MalformedSerializedCommitException(string detail) : base($"Malformed serialized commit envelope: {detail}") =>
        Detail = detail;

    public MalformedSerializedCommitException(string detail, Exception innerException)
        : base($"Malformed serialized commit envelope: {detail}", innerException) =>
        Detail = detail;

    /// <summary>The sanitized structural reason (never includes payload bytes).</summary>
    public string Detail { get; }
}
