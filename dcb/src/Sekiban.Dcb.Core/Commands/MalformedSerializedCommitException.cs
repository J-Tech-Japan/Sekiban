namespace Sekiban.Dcb.Commands;

/// <summary>The closed set of structural (shape) failures for a serialized-commit envelope. No value carries request data.</summary>
public enum SerializedCommitShapeError
{
    /// <summary>The request root is not a JSON object.</summary>
    NonObjectRoot,
    /// <summary>A single <c>version</c> is present but is not a JSON integer.</summary>
    VersionNotInteger,
    /// <summary>The exact <c>version</c> discriminator appears more than once.</summary>
    DuplicateVersion,
    /// <summary>A case-variant of <c>version</c> (e.g. <c>Version</c>) is present; only the exact spelling is allowed.</summary>
    AmbiguousVersionCasing,
    /// <summary>The bytes are not well-formed JSON (raw parser detail is deliberately NOT surfaced).</summary>
    UnreadableJson,
    /// <summary>The legacy unversioned payload does not bind to the contract shape.</summary>
    LegacyPayloadInvalid,
    /// <summary>The known-version (V1) payload does not bind to the contract shape.</summary>
    VersionedPayloadInvalid,
    /// <summary>One or both official collection members are absent from the raw envelope.</summary>
    MissingOfficialCollectionMembers,
    /// <summary>A legacy client alias such as <c>candidates</c> or <c>consistency</c> is present.</summary>
    AliasedCollectionMember,
    /// <summary>An official top-level collection member appears more than once.</summary>
    DuplicateCollectionMember,
    /// <summary>A case-variant of an official top-level collection member is present.</summary>
    AmbiguousCollectionMemberCasing,
    /// <summary>The V2-only <c>expectedTagPositions</c> collection is absent.</summary>
    MissingV2ExpectedTagPositions,
    /// <summary>The V2-only <c>expectedTagPositions</c> collection was supplied to a legacy or V1 envelope.</summary>
    UnexpectedV2ExpectedTagPositions
}

/// <summary>
///     SEK-G17 typed shape failure for a serialized-commit envelope whose STRUCTURE is invalid. It replaces the previous
///     null-reference 500 with a clear, typed contract error.
///     <para>
///         SECRET-SAFE by construction: the failure is described ONLY by a closed <see cref="SerializedCommitShapeError" />
///         code and a fixed message derived from it. It NEVER embeds the offending JSON, property names/keys, payload
///         bytes, base64 text, type names, or a raw System.Text.Json exception (there is no inner cause). This prevents a
///         hostile request from smuggling its content out through the error surface.
///     </para>
///     Distinct from <see cref="UnsupportedSerializedCommitEnvelopeVersionException" /> (a well-formed but unknown version).
///     A malformed shape is never routed to the executor or store.
/// </summary>
public sealed class MalformedSerializedCommitException : Exception
{
    public MalformedSerializedCommitException(SerializedCommitShapeError reason) : base(MessageFor(reason)) => Reason = reason;

    /// <summary>The closed, request-data-free reason code.</summary>
    public SerializedCommitShapeError Reason { get; }

    private static string MessageFor(SerializedCommitShapeError reason) => reason switch
    {
        SerializedCommitShapeError.NonObjectRoot => "Malformed serialized commit envelope: the request root is not a JSON object.",
        SerializedCommitShapeError.VersionNotInteger => "Malformed serialized commit envelope: the 'version' discriminator is not a JSON integer.",
        SerializedCommitShapeError.DuplicateVersion => "Malformed serialized commit envelope: the 'version' discriminator appears more than once.",
        SerializedCommitShapeError.AmbiguousVersionCasing => "Malformed serialized commit envelope: a case-variant of 'version' is present; the discriminator must be exactly 'version'.",
        SerializedCommitShapeError.UnreadableJson => "Malformed serialized commit envelope: the request is not well-formed JSON.",
        SerializedCommitShapeError.LegacyPayloadInvalid => "Malformed serialized commit envelope: the legacy unversioned payload does not match the contract shape.",
        SerializedCommitShapeError.VersionedPayloadInvalid => "Malformed serialized commit envelope: the versioned (V1) payload does not match the contract shape.",
        SerializedCommitShapeError.MissingOfficialCollectionMembers => "Malformed serialized commit envelope: both required official collection members must be present exactly once.",
        SerializedCommitShapeError.AliasedCollectionMember => "Malformed serialized commit envelope: aliased collection member names are not accepted.",
        SerializedCommitShapeError.DuplicateCollectionMember => "Malformed serialized commit envelope: an official collection member appears more than once.",
        SerializedCommitShapeError.AmbiguousCollectionMemberCasing => "Malformed serialized commit envelope: official collection member names must use exact camelCase spelling.",
        SerializedCommitShapeError.MissingV2ExpectedTagPositions => "Malformed serialized commit envelope: V2 requires the expectedTagPositions collection member.",
        SerializedCommitShapeError.UnexpectedV2ExpectedTagPositions => "Malformed serialized commit envelope: expectedTagPositions is valid only for V2.",
        _ => "Malformed serialized commit envelope."
    };
}
