using Sekiban.Dcb.MultiProjections;
namespace Sekiban.Dcb.Storage.Checkpoints;

// SEK-G20: shared-checkpoint generation + tombstone + expected-generation CAS.
//
// These are NEW versioned DTOs for an OPTIONAL, out-of-band checkpoint-store surface
// (see IGenerationAwareCheckpointStore). They deliberately do NOT add any field to the frozen positional records
// MultiProjectionStateRecord / MultiProjectionStateWriteRequest — the generation / tombstone / per-mutation token
// metadata travels here instead. The protocol is a TWO-LAYER CAS: a monotonic rebuild-epoch generation PLUS an opaque
// per-mutation revision token (Postgres/SQLite revision, Cosmos ETag, DynamoDB version). Every conditional operation
// compares the EXACT token; a generation-only comparison is explicitly NOT a CAS and must be rejected.

/// <summary>Lifecycle state of a shared checkpoint row in the generation/tombstone state machine.</summary>
public enum CheckpointLifecycle
{
    /// <summary>The row holds an authoritative payload at its generation. Product persists CAS against this.</summary>
    Active = 0,

    /// <summary>
    ///     The row was durably invalidated (generation bumped) and holds no authoritative payload. A cluster that
    ///     observes a tombstone must arm its rebuild barrier and produce a rebuilt commit; stale writers CAS-reject.
    /// </summary>
    Tombstoned = 1
}

/// <summary>Capability kinds an <see cref="IMultiProjectionStateStore" /> may advertise for the checkpoint surface.</summary>
public enum CheckpointCapabilityKind
{
    /// <summary>Fail-closed sentinel: silence is never read as a capability.</summary>
    Unknown = 0,

    /// <summary>Two-layer CAS (generation epoch + exact per-mutation token) with bump+tombstone invalidation.</summary>
    GenerationTombstoneCas = 1
}

/// <summary>
///     Describes which checkpoint capability kinds a live state-store instance supports. Mirrors the G15/G16
///     <c>WriteConditionCapabilityDescriptor</c> discipline: resolved from the LIVE instance the container built (never a
///     type name), and a composite reports a kind only when EVERY underlying store supports it.
/// </summary>
public sealed record CheckpointStoreCapabilityDescriptor(
    IReadOnlySet<CheckpointCapabilityKind> SupportedKinds,
    string ProviderName)
{
    /// <summary>True only for a real, non-<see cref="CheckpointCapabilityKind.Unknown" /> kind that is present.</summary>
    public bool Supports(CheckpointCapabilityKind kind) =>
        kind != CheckpointCapabilityKind.Unknown && SupportedKinds.Contains(kind);

    public static CheckpointStoreCapabilityDescriptor None(string providerName) =>
        new(new HashSet<CheckpointCapabilityKind>(), providerName);

    public static CheckpointStoreCapabilityDescriptor Supporting(
        string providerName,
        params CheckpointCapabilityKind[] kinds) =>
        new(new HashSet<CheckpointCapabilityKind>(kinds.Where(k => k != CheckpointCapabilityKind.Unknown)), providerName);

    /// <summary>
    ///     Composite propagation: a kind is supported only when EVERY underlying descriptor supports it (intersection).
    ///     An empty underlying set supports nothing.
    /// </summary>
    public static CheckpointStoreCapabilityDescriptor Intersect(
        string providerName,
        IReadOnlyCollection<CheckpointStoreCapabilityDescriptor> underlying)
    {
        if (underlying.Count == 0)
        {
            return None(providerName);
        }

        var intersection = new HashSet<CheckpointCapabilityKind>(underlying.First().SupportedKinds);
        foreach (var descriptor in underlying.Skip(1))
        {
            intersection.IntersectWith(descriptor.SupportedKinds);
        }
        intersection.Remove(CheckpointCapabilityKind.Unknown);
        return new CheckpointStoreCapabilityDescriptor(intersection, providerName);
    }
}

/// <summary>Advertises checkpoint-store capabilities from a LIVE instance (never inferred from a type name).</summary>
public interface ICheckpointStoreCapabilityProvider
{
    CheckpointStoreCapabilityDescriptor DescribeCheckpointCapability();
}

/// <summary>
///     An atomic snapshot of a checkpoint row's control plane: its generation (rebuild epoch), its opaque exact-CAS
///     revision token, its lifecycle, and — when Active — the payload record metadata. Read BEFORE any payload binding on
///     a capable store so a fresh activation can decide tombstone→rebuild vs active→adopt as one unit.
/// </summary>
public sealed record CheckpointSlot(
    bool Exists,
    long Generation,
    string Revision,
    CheckpointLifecycle Lifecycle,
    MultiProjectionStateRecord? Record)
{
    /// <summary>The canonical "row does not exist yet" slot — the precondition for a first-ever create.</summary>
    public static CheckpointSlot Absent { get; } =
        new(false, 0, string.Empty, CheckpointLifecycle.Active, null);

    public bool IsActive => Exists && Lifecycle == CheckpointLifecycle.Active;
    public bool IsTombstoned => Exists && Lifecycle == CheckpointLifecycle.Tombstoned;
}

/// <summary>
///     The exact precondition a conditional operation asserts about the current row. Either the row must be ABSENT (a
///     first-ever create), or it must match the EXACT (generation, revision, lifecycle) observed by a prior read. A
///     bare generation match is NOT a valid CAS and providers must reject it.
/// </summary>
public sealed record CheckpointExpectation(
    bool ExpectAbsent,
    long ExpectedGeneration,
    string? ExpectedRevision,
    CheckpointLifecycle ExpectedLifecycle)
{
    /// <summary>Precondition for a first-ever checkpoint create: the row must not exist.</summary>
    public static CheckpointExpectation Absent { get; } =
        new(true, 0, null, CheckpointLifecycle.Active);

    /// <summary>Precondition equal to the exact control-plane identity of a previously read slot.</summary>
    public static CheckpointExpectation FromSlot(CheckpointSlot slot) => slot.Exists
        ? new CheckpointExpectation(false, slot.Generation, slot.Revision, slot.Lifecycle)
        : Absent;

    /// <summary>
    ///     The shared invalidate/rebuilt-commit guard: a mutation that advances an EXISTING row requires an exact numeric
    ///     revision token (never expected-absence). Returns false — the caller returns Corruption — for an ExpectAbsent or
    ///     an unparseable revision. Centralised so every provider validates identically.
    /// </summary>
    public bool TryGetExactRevision(out long revision)
    {
        revision = 0;
        return !ExpectAbsent && long.TryParse(ExpectedRevision, out revision);
    }
}

/// <summary>Closed outcome set for a conditional checkpoint operation.</summary>
public enum CheckpointCasStatus
{
    /// <summary>The CAS held and the row was mutated to its next state.</summary>
    Committed,

    /// <summary>
    ///     The precondition did not hold: another writer moved the row. This is a REFETCH signal, never a G14 fault.
    ///     The current slot is returned so the caller can refetch and re-decide (rebuild vs adopt).
    /// </summary>
    ConditionRejected,

    /// <summary>The store does not implement the generation/tombstone CAS capability.</summary>
    ConditionNotSupported,

    /// <summary>The provider failed the operation before any durable commit (safe to retry with a fresh read).</summary>
    ProviderFailure,

    /// <summary>The row is in an impossible/corrupt control-plane state (fail closed; never a silent overwrite).</summary>
    Corruption,

    /// <summary>
    ///     A response was lost AFTER a possible durable commit and bounded independent re-read could not resolve the
    ///     winner. Typed retryable; the caller must re-read (never a blind retry with the stale token).
    /// </summary>
    InDoubt
}

/// <summary>
///     Closed reason set for an <see cref="CheckpointCasStatus.InDoubt" /> outcome. Both values are typed retryable (the
///     caller must re-read, never blind-retry with the stale token); neither is ever a false success. The distinction lets
///     a caller/telemetry tell WHY the winner is unknown after a commit-capable boundary was crossed.
/// </summary>
public enum CheckpointInDoubtReason
{
    /// <summary>
    ///     At least one bounded independent re-read SUCCEEDED but none confirmed OUR own write (the authoritative row shows
    ///     a not-yet-visible or a foreign state), so whether our write committed is genuinely unknown.
    /// </summary>
    AmbiguousAfterWrite,

    /// <summary>
    ///     EVERY bounded independent re-read failed/timed out — the authority was unreachable, so verification could not be
    ///     performed at all. Unreadable authority after a possible commit is NEVER a known pre-commit failure.
    /// </summary>
    VerificationUnavailable
}

/// <summary>
///     The result of a conditional checkpoint operation. On <see cref="CheckpointCasStatus.Committed" />, <see
///     cref="ResultingSlot" /> carries the new control-plane identity. On <see cref="CheckpointCasStatus.ConditionRejected" />,
///     <see cref="CurrentSlot" /> carries the row as it actually is (the refetch signal). On <see
///     cref="CheckpointCasStatus.InDoubt" />, <see cref="InDoubtReason" /> is the closed typed reason. Provider causes are
///     attached only when present and must be surfaced secret-safe by callers (see <see cref="SafeDescribe" />).
/// </summary>
public sealed record CheckpointCasOutcome(
    CheckpointCasStatus Status,
    CheckpointSlot? CurrentSlot = null,
    CheckpointSlot? ResultingSlot = null,
    Exception? Cause = null,
    CheckpointInDoubtReason? InDoubtReason = null)
{
    public static CheckpointCasOutcome Committed(CheckpointSlot resultingSlot) =>
        new(CheckpointCasStatus.Committed, ResultingSlot: resultingSlot);

    public static CheckpointCasOutcome Rejected(CheckpointSlot currentSlot) =>
        new(CheckpointCasStatus.ConditionRejected, CurrentSlot: currentSlot);

    public static CheckpointCasOutcome NotSupported() =>
        new(CheckpointCasStatus.ConditionNotSupported);

    public static CheckpointCasOutcome ProviderFailed(Exception cause) =>
        new(CheckpointCasStatus.ProviderFailure, Cause: cause);

    public static CheckpointCasOutcome Corrupt(Exception? cause = null) =>
        new(CheckpointCasStatus.Corruption, Cause: cause);

    public static CheckpointCasOutcome Doubt(CheckpointInDoubtReason reason, Exception? cause = null) =>
        new(CheckpointCasStatus.InDoubt, Cause: cause, InDoubtReason: reason);

    /// <summary>Whether the caller may safely retry (after a fresh read): InDoubt and ProviderFailure are both retryable.</summary>
    public bool IsRetryable => Status is CheckpointCasStatus.InDoubt or CheckpointCasStatus.ProviderFailure;

    /// <summary>
    ///     A SECRET-SAFE one-line description: the status, the typed InDoubt reason, and the EXCEPTION TYPE chain only —
    ///     never any exception message (which may embed a connection string, key, or row value). Recursively walks inner
    ///     exceptions emitting type names alone.
    /// </summary>
    public string SafeDescribe()
    {
        var reason = InDoubtReason is { } r ? $"/{r}" : string.Empty;
        var causeChain = string.Empty;
        for (Exception? e = Cause; e is not null; e = e.InnerException)
        {
            causeChain += (causeChain.Length == 0 ? " cause=" : "->") + e.GetType().Name;
        }
        return $"{Status}{reason}{causeChain}";
    }
}
