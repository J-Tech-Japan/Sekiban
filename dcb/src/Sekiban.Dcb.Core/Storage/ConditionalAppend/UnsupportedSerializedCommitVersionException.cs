namespace Sekiban.Dcb.Storage;

/// <summary>
///     The typed failure raised (fail-closed, before EventId allocation / serialization / any store call) when a
///     <c>SerializedConditionalCommitRequest</c> carries an unsupported wire <c>Version</c>. An unknown version is never
///     interpreted optimistically.
/// </summary>
public sealed class UnsupportedSerializedCommitVersionException : Exception
{
    public UnsupportedSerializedCommitVersionException(int requestedVersion, int supportedVersion)
        : base(
            $"Serialized conditional commit request version {requestedVersion} is not supported "
            + $"(this runtime supports version {supportedVersion}).")
    {
        RequestedVersion = requestedVersion;
        SupportedVersion = supportedVersion;
    }

    public int RequestedVersion { get; }
    public int SupportedVersion { get; }
}
