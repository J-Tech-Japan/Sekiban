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
    VersionedPayloadInvalid
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
        _ => "Malformed serialized commit envelope."
    };
}
