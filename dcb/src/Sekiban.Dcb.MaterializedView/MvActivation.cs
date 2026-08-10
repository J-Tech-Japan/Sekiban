namespace Sekiban.Dcb.MaterializedView;

/// <summary>Typed reasons why a materialized-view candidate cannot become active.</summary>
public enum MvActivationFailureReason
{
    None = 0,
    CandidateMissing = 1,
    IdentityMismatch = 2,
    TargetUnknown = 3,
    CurrentCheckpointUnknown = 4,
    BehindTarget = 5,
    UnsafeLifecycle = 6,
    Faulted = 7,
    MissingProvenance = 8,
    CandidateStateMismatch = 9,
    AlreadyActive = 10,
    ExpectedActiveConflict = 11,
    ExpectedGenerationConflict = 12,
    ConcurrentSuperseded = 13,
    ProviderFailure = 14
}

/// <summary>Provider-neutral result of evaluating a candidate before any active-pointer mutation.</summary>
public sealed record MvActivationEligibilityResult(
    bool IsEligible,
    MvActivationFailureReason FailureReason,
    string Message)
{
    public static MvActivationEligibilityResult Eligible() =>
        new(true, MvActivationFailureReason.None, "Candidate is eligible for activation.");

    public static MvActivationEligibilityResult Rejected(
        MvActivationFailureReason reason,
        string message) =>
        new(false, reason, message);
}

/// <summary>
///     Immutable candidate snapshot passed to the provider CAS operation. The encoded checkpoint values are the
///     exact values observed during eligibility evaluation, so a concurrent target/current update cannot be silently
///     activated by a later compare-and-switch.
/// </summary>
public sealed record MvActivationRequest(
    string ServiceId,
    string ViewName,
    int ViewVersion,
    int? ExpectedActiveVersion,
    long ExpectedActiveGeneration,
    int CandidateCount,
    MvStatus ExpectedStatus,
    string ExpectedCurrentCheckpointTruth,
    string ExpectedTargetCheckpointTruth);

/// <summary>Result of the provider-atomic active-pointer operation.</summary>
public sealed record MvActivationResult(
    bool Succeeded,
    MvActivationFailureReason FailureReason,
    string Message,
    long? NewGeneration = null)
{
    public bool IsConflict =>
        FailureReason is MvActivationFailureReason.ExpectedActiveConflict
            or MvActivationFailureReason.ExpectedGenerationConflict
            or MvActivationFailureReason.ConcurrentSuperseded;

    public static MvActivationResult Success(long generation) =>
        new(true, MvActivationFailureReason.None, "Candidate became active.", generation);

    public static MvActivationResult Rejected(
        MvActivationFailureReason reason,
        string message) =>
        new(false, reason, message);
}

/// <summary>
///     Common fail-closed eligibility evaluator. It deliberately consumes only registry truth and the active
///     pointer snapshot; it never consults G24 sampled status or an event-store count.
/// </summary>
public static class MvActivationEligibility
{
    public static (MvActivationEligibilityResult Eligibility, MvActivationRequest? Request) Evaluate(
        string serviceId,
        string viewName,
        int viewVersion,
        IReadOnlyList<MvRegistryEntry> entries,
        MvActiveEntry? active)
    {
        if (entries.Count == 0)
        {
            return Reject(
                MvActivationFailureReason.CandidateMissing,
                "The candidate has no registered materialized-view rows.");
        }

        foreach (var entry in entries)
        {
            var rejection = EvaluateEntry(serviceId, viewName, viewVersion, entry);
            if (rejection is not null)
            {
                return (rejection, null);
            }
        }

        var first = entries[0];
        var expectedCurrentTruth = MvCheckpointTruthCodec.Encode(first.CurrentCheckpointTruth);
        var expectedTargetTruth = MvCheckpointTruthCodec.Encode(first.TargetCheckpointTruth);
        if (entries.Any(entry =>
                !string.Equals(MvCheckpointTruthCodec.Encode(entry.CurrentCheckpointTruth), expectedCurrentTruth, StringComparison.Ordinal) ||
                !string.Equals(MvCheckpointTruthCodec.Encode(entry.TargetCheckpointTruth), expectedTargetTruth, StringComparison.Ordinal)))
        {
            return Reject(
                MvActivationFailureReason.CandidateStateMismatch,
                "Materialized-view registry rows do not share one checkpoint snapshot.");
        }

        var activeRejection = EvaluateActivePointer(serviceId, viewName, viewVersion, active);
        if (activeRejection is not null)
        {
            return (activeRejection, null);
        }

        var request = new MvActivationRequest(
            serviceId,
            viewName,
            viewVersion,
            active?.ActiveVersion,
            active?.Generation ?? 0,
            entries.Count,
            MvStatus.Ready,
            expectedCurrentTruth,
            expectedTargetTruth);
        return (MvActivationEligibilityResult.Eligible(), request);
    }

    private static MvActivationEligibilityResult? EvaluateEntry(
        string serviceId,
        string viewName,
        int viewVersion,
        MvRegistryEntry entry) =>
        EvaluateEntryIdentity(serviceId, viewName, viewVersion, entry) ??
        EvaluateEntryLifecycle(entry) ??
        EvaluateEntryTruth(entry) ??
        EvaluateEntryPositionConsistency(entry) ??
        EvaluateEntryOrdering(entry);

    private static MvActivationEligibilityResult? EvaluateEntryIdentity(
        string serviceId,
        string viewName,
        int viewVersion,
        MvRegistryEntry entry) =>
        string.Equals(entry.ServiceId, serviceId, StringComparison.Ordinal) &&
        string.Equals(entry.ViewName, viewName, StringComparison.Ordinal) &&
        entry.ViewVersion == viewVersion
            ? null
            : MvActivationEligibilityResult.Rejected(
                MvActivationFailureReason.IdentityMismatch,
                "The candidate registry row does not match the requested service, view, and version.");

    private static MvActivationEligibilityResult? EvaluateEntryLifecycle(MvRegistryEntry entry)
    {
        if (entry.Status == MvStatus.Faulted)
        {
            return MvActivationEligibilityResult.Rejected(
                MvActivationFailureReason.Faulted,
                "A faulted materialized-view candidate cannot become active.");
        }

        return entry.Status == MvStatus.Ready
            ? null
            : MvActivationEligibilityResult.Rejected(
                MvActivationFailureReason.UnsafeLifecycle,
                $"The candidate lifecycle is '{entry.Status}', but only Ready candidates may become active.");
    }

    private static MvActivationEligibilityResult? EvaluateEntryTruth(MvRegistryEntry entry)
    {
        if (!entry.TargetCheckpointTruth.IsKnown)
        {
            return MvActivationEligibilityResult.Rejected(
                MvActivationFailureReason.TargetUnknown,
                "The candidate target checkpoint is Unknown.");
        }

        if (!entry.CurrentCheckpointTruth.IsKnown)
        {
            return MvActivationEligibilityResult.Rejected(
                MvActivationFailureReason.CurrentCheckpointUnknown,
                "The candidate current checkpoint is Unknown.");
        }

        return HasActivationProvenance(entry)
            ? null
            : MvActivationEligibilityResult.Rejected(
                MvActivationFailureReason.MissingProvenance,
                "Activation requires authoritative target provenance and non-legacy current provenance.");
    }

    private static bool HasActivationProvenance(MvRegistryEntry entry) =>
        entry.TargetCheckpointTruth.Provenance?.Kind == MvCheckpointProvenanceKind.AuthoritativeTargetCapture &&
        entry.CurrentCheckpointTruth.Provenance is not null &&
        entry.CurrentCheckpointTruth.Provenance.Kind != MvCheckpointProvenanceKind.LegacyCompatibility;

    private static MvActivationEligibilityResult? EvaluateEntryPositionConsistency(MvRegistryEntry entry)
    {
        var currentMatches = entry.CurrentPosition is null ||
            string.Equals(entry.CurrentPosition, entry.CurrentCheckpointTruth.PositionValue, StringComparison.Ordinal);
        var targetMatches = entry.TargetPosition is null ||
            string.Equals(entry.TargetPosition, entry.TargetCheckpointTruth.PositionValue, StringComparison.Ordinal);
        return currentMatches && targetMatches
            ? null
            : MvActivationEligibilityResult.Rejected(
                MvActivationFailureReason.CandidateStateMismatch,
                "Legacy position fields disagree with typed checkpoint truth.");
    }

    private static MvActivationEligibilityResult? EvaluateEntryOrdering(MvRegistryEntry entry) =>
        entry.CurrentCheckpointTruth.Satisfies(entry.TargetCheckpointTruth)
            ? null
            : MvActivationEligibilityResult.Rejected(
                MvActivationFailureReason.BehindTarget,
                "The candidate current checkpoint is behind its captured target.");

    private static MvActivationEligibilityResult? EvaluateActivePointer(
        string serviceId,
        string viewName,
        int viewVersion,
        MvActiveEntry? active)
    {
        if (active is null)
        {
            return null;
        }

        if (!string.Equals(active.ServiceId, serviceId, StringComparison.Ordinal) ||
            !string.Equals(active.ViewName, viewName, StringComparison.Ordinal))
        {
            return MvActivationEligibilityResult.Rejected(
                MvActivationFailureReason.IdentityMismatch,
                "The active pointer identity does not match the requested service and view.");
        }

        return active.ActiveVersion == viewVersion
            ? MvActivationEligibilityResult.Rejected(
                MvActivationFailureReason.AlreadyActive,
                "The candidate version is already active.")
            : null;
    }

    private static (MvActivationEligibilityResult Eligibility, MvActivationRequest? Request) Reject(
        MvActivationFailureReason reason,
        string message) =>
        (MvActivationEligibilityResult.Rejected(reason, message), null);
}
