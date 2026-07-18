namespace Sekiban.Dcb.Storage;

/// <summary>
///     The single observable outcome machine of a conditional (unique-key) append. Exactly one state is reported per
///     attempt:
///     <list type="bullet">
///         <item>
///             <see cref="Appended" /> — this attempt won the key and durably wrote the event; the receipt carries the
///             winner's identity.
///         </item>
///         <item>
///             <see cref="AlreadyCommittedSameOperation" /> — the key was already claimed by the SAME operation
///             (fingerprint matches); nothing was written and the receipt carries the ORIGINAL winner's identity.
///         </item>
///         <item>
///             <see cref="KeyReuseConflict" /> — the key exists but for a DIFFERENT operation (fingerprint differs);
///             surfaced as <see cref="KeyReuseConflictException" />.
///         </item>
///         <item>
///             <see cref="ConditionNotSupported" /> — the resolved store cannot enforce this write-condition kind;
///             surfaced (fail-closed, before any work) as <see cref="ConditionNotSupportedException" />.
///         </item>
///     </list>
///     Operation identity ALONE is never proof of "same operation": <see cref="AlreadyCommittedSameOperation" /> is only
///     reported when the persisted fingerprint matches; otherwise it is <see cref="KeyReuseConflict" />.
/// </summary>
public enum ConditionalAppendStatus
{
    /// <summary>Never reported; fail-closed default so an uninitialised value can never read as a success.</summary>
    Unknown = 0,
    Appended = 1,
    AlreadyCommittedSameOperation = 2,
    KeyReuseConflict = 3,
    ConditionNotSupported = 4
}
