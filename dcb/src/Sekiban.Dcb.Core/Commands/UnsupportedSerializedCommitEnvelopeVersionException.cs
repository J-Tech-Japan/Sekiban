namespace Sekiban.Dcb.Commands;

/// <summary>
///     SEK-G17 typed failure raised (fail-closed) when a <see cref="VersionedSerializedCommitRequest" /> envelope carries a
///     version the runtime does not support. It is produced during the two-phase acceptance's FIRST phase — after the raw
///     version discriminator is read but BEFORE any typed payload binding, base64 decode, tag reservation, EventId
///     allocation, or executor/store call. An unknown version is never interpreted optimistically.
///     <para>
///         Distinct from <see cref="MalformedSerializedCommitException" /> (a shape error) and from G15's
///         <c>Sekiban.Dcb.Storage.UnsupportedSerializedCommitVersionException</c> (the single-event conditional path).
///     </para>
/// </summary>
public sealed class UnsupportedSerializedCommitEnvelopeVersionException : Exception
{
    public UnsupportedSerializedCommitEnvelopeVersionException(int requestedVersion, int supportedVersion)
        : base(
            $"Serialized commit envelope version {requestedVersion} is not supported "
            + $"(this runtime supports version {supportedVersion}).")
    {
        RequestedVersion = requestedVersion;
        SupportedVersion = supportedVersion;
    }

    public int RequestedVersion { get; }
    public int SupportedVersion { get; }
}
