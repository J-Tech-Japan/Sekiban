using ResultBoxes;
namespace Sekiban.Dcb.Commands;

/// <summary>
///     SEK-G17 OPTIONAL, additive acceptance surface for the serialized-commit wire contract. It is the boundary an
///     endpoint may host to accept raw UTF-8 request bytes and turn them into a commit, replacing ad-hoc typed model
///     binding (which produced a null-reference 500 on a shape mismatch) with explicit two-phase handling and typed errors.
///     <para>
///         This is a NEW interface — no member is added to <see cref="Sekiban.Dcb.Actors.ISerializedSekibanDcbExecutor" />
///         or any other existing contract. Hosting it is opt-in; existing in-process callers of the executor are unchanged.
///     </para>
/// </summary>
public interface ISerializedCommitAcceptor
{
    /// <summary>
    ///     Accepts a raw serialized-commit request. Phase 1 reads the <c>version</c> discriminator before any typed binding
    ///     or side effect; phase 2 binds the resolved shape and routes a supported version to the executor with identical
    ///     semantics. The error slot carries a typed
    ///     <see cref="UnsupportedSerializedCommitEnvelopeVersionException" /> or
    ///     <see cref="MalformedSerializedCommitException" />; a null-reference failure is never surfaced.
    /// </summary>
    Task<ResultBox<SerializedCommitResult>> AcceptAsync(
        ReadOnlyMemory<byte> utf8Json,
        CancellationToken cancellationToken = default);
}
