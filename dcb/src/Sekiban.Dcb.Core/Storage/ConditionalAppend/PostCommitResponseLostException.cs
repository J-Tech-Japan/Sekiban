namespace Sekiban.Dcb.Storage;

/// <summary>
///     INTERNAL provider→orchestrator signal (never observed by callers): a provider durably committed the conditional
///     claim but then LOST THE RESPONSE (a transport error or cancellation on the return path, after the commit
///     succeeded). It is distinct from a pre-commit failure — the provider raises it ONLY when it knows the commit
///     completed. <see cref="ConditionalAppendExecution" /> always catches it and resolves the outcome authoritatively
///     (bounded winner read + fingerprint + committed-state verification): it returns
///     <see cref="ConditionalAppendStatus.AlreadyCommittedSameOperation" /> on proof, otherwise a typed
///     <see cref="ConditionalAppendInDoubtException" /> with reason <see cref="ConditionalAppendInDoubtReason.AmbiguousAfterWrite" />
///     preserving the original cause. It therefore never escapes to the caller, and the raw transport exception is never
///     the observable result of a post-commit response loss.
///     <see cref="OriginalCause" /> is the real transport/cancellation exception; it is exposed as
///     <see cref="Exception.InnerException" /> only through the typed in-doubt/AlreadyCommitted resolution, never raw.
/// </summary>
internal sealed class PostCommitResponseLostException : Exception
{
    public PostCommitResponseLostException(Exception originalCause)
        : base("The conditional claim committed durably but the response was lost.", originalCause)
    {
        OriginalCause = originalCause;
    }

    public Exception OriginalCause { get; }
}
