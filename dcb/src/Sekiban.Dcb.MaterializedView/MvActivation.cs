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
    ProviderFailure = 14,
    TransitionNotAllowed = 15,
    RetryableConcurrency = 16,
    RetryExhausted = 17
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
    string ExpectedTargetCheckpointTruth)
{
    /// <summary>Audit classification inferred by the eligibility boundary; callers cannot force it.</summary>
    public MvSwitchKind SwitchKind { get; init; } = MvSwitchKind.Initial;
}

/// <summary>Durable classification of an active-pointer transition.</summary>
public enum MvSwitchKind
{
    Initial = 0,
    Forward = 1,
    Reverse = 2,
    Forced = 3,
    Legacy = 4
}

/// <summary>
///     Separate break-glass request. It is deliberately reverse-only and contains no ordinary-forward mode flag.
///     Providers may waive checkpoint truth only; identity, lifecycle, existence, and pointer fencing remain required.
/// </summary>
public sealed record MvForcedReverseRequest(
    string ServiceId,
    string ViewName,
    int ViewVersion,
    int ExpectedActiveVersion,
    long ExpectedActiveGeneration,
    int CandidateCount,
    MvStatus ExpectedStatus,
    string Reason,
    DateTimeOffset RequestedAtUtc);

public static class MvForcedReverseValidation
{
    public static MvActivationResult? Validate(MvForcedReverseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ServiceId) || string.IsNullOrWhiteSpace(request.ViewName))
        {
            return MvActivationResult.Rejected(MvActivationFailureReason.IdentityMismatch, "Forced reverse requires an exact service and view identity.");
        }

        if (request.ViewVersion < 0 || request.ExpectedActiveVersion <= request.ViewVersion)
        {
            return MvActivationResult.Rejected(MvActivationFailureReason.IdentityMismatch, "Forced switching is reverse-only.");
        }

        if (request.ExpectedActiveGeneration < 0)
        {
            return MvActivationResult.Rejected(MvActivationFailureReason.ExpectedGenerationConflict, "The expected active generation cannot be negative.");
        }

        if (request.CandidateCount <= 0 || request.ExpectedStatus != MvStatus.Ready)
        {
            return MvActivationResult.Rejected(MvActivationFailureReason.UnsafeLifecycle, "Forced reverse requires a retained Ready candidate.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1024)
        {
            return MvActivationResult.Rejected(MvActivationFailureReason.ProviderFailure, "Forced reverse requires a non-empty reason of at most 1024 characters.");
        }

        return null;
    }
}

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

    /// <summary>
    ///     True when the provider reported a transient concurrency outcome and the caller may retry from a fresh
    ///     transaction/read boundary. The original request is intentionally not treated as an authorization to
    ///     retry a superseded snapshot.
    /// </summary>
    public bool IsRetryableConcurrency =>
        FailureReason is MvActivationFailureReason.RetryableConcurrency
            or MvActivationFailureReason.RetryExhausted;

    public int AttemptCount { get; init; }

    public static MvActivationResult Success(long generation) =>
        new(true, MvActivationFailureReason.None, "Candidate became active.", generation);

    public static MvActivationResult Rejected(
        MvActivationFailureReason reason,
        string message) =>
        new(false, reason, message);

    public static MvActivationResult ActiveStatusRestored(long generation) =>
        new(true, MvActivationFailureReason.None, "The serving materialized-view status was restored.", generation);
}

/// <summary>
///     One immutable row expectation for the additive serving-status restore operation. The current checkpoint is a
///     minimum observed value: a monotonic advance while the provider waits for the row locks is valid. The target
///     checkpoint is exact and is compared as authoritative typed truth.
/// </summary>
public sealed record MvActiveStatusRestoreRow(
    string LogicalTable,
    string PhysicalTable,
    IReadOnlyList<MvStatus> AllowedStatuses,
    string MinimumCurrentCheckpointTruth,
    string ExpectedTargetCheckpointTruth)
{
    public static MvActiveStatusRestoreRow FromEntry(MvRegistryEntry entry) =>
        new(
            entry.LogicalTable,
            entry.PhysicalTable,
            [MvStatus.CatchingUp, MvStatus.Ready, MvStatus.Active],
            MvCheckpointTruthCodec.Encode(entry.CurrentCheckpointTruth),
            MvCheckpointTruthCodec.Encode(entry.TargetCheckpointTruth));
}

/// <summary>
///     Immutable, generation-fenced request to restore the status of the currently serving view version. The row
///     list pins the complete logical/physical table identity and count; it is never interpreted as permission to
///     change checkpoint, progress, pointer, or generation data.
/// </summary>
public sealed record MvActiveStatusRestoreRequest(
    string ServiceId,
    string ViewName,
    int ViewVersion,
    long ExpectedActiveGeneration,
    int ExpectedTableCount,
    IReadOnlyList<MvActiveStatusRestoreRow> Rows)
{
    /// <summary>Finite provider retry bound for transient deadlock/serialization/busy outcomes.</summary>
    public int MaxAttempts { get; init; } = 3;

    public static MvActiveStatusRestoreRequest FromEntries(
        string serviceId,
        string viewName,
        int viewVersion,
        long expectedActiveGeneration,
        IReadOnlyList<MvRegistryEntry> entries) =>
        new(
            serviceId,
            viewName,
            viewVersion,
            expectedActiveGeneration,
            entries.Count,
            entries.Select(MvActiveStatusRestoreRow.FromEntry).ToList());
}

/// <summary>Validation shared by all provider implementations of serving-status restoration.</summary>
public static class MvActiveStatusRestoreValidation
{
    public static MvActivationResult? Validate(MvActiveStatusRestoreRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ServiceId) || string.IsNullOrWhiteSpace(request.ViewName) || request.ViewVersion < 0)
        {
            return MvActivationResult.Rejected(
                MvActivationFailureReason.IdentityMismatch,
                "Serving-status restore requires an exact service, view, and non-negative version.");
        }

        if (request.ExpectedActiveGeneration < 0)
        {
            return MvActivationResult.Rejected(
                MvActivationFailureReason.ExpectedGenerationConflict,
                "The expected active generation cannot be negative.");
        }

        if (request.Rows is null || request.ExpectedTableCount <= 0 || request.Rows.Count != request.ExpectedTableCount)
        {
            return MvActivationResult.Rejected(
                MvActivationFailureReason.CandidateMissing,
                "Serving-status restore requires the complete non-empty registry row set.");
        }

        if (request.MaxAttempts is < 1 or > 8)
        {
            return MvActivationResult.Rejected(
                MvActivationFailureReason.ProviderFailure,
                "Serving-status restore requires a finite retry bound between one and eight attempts.");
        }

        var logicalTables = new HashSet<string>(StringComparer.Ordinal);
        var physicalTables = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in request.Rows)
        {
            if (string.IsNullOrWhiteSpace(row.LogicalTable) ||
                string.IsNullOrWhiteSpace(row.PhysicalTable) ||
                !logicalTables.Add(row.LogicalTable) ||
                !physicalTables.Add(row.PhysicalTable))
            {
                return MvActivationResult.Rejected(
                    MvActivationFailureReason.IdentityMismatch,
                    "Serving-status restore requires unique logical and non-empty physical table identities.");
            }

            if (row.AllowedStatuses is null || row.AllowedStatuses.Count == 0 ||
                row.AllowedStatuses.Any(status => status is not (MvStatus.CatchingUp or MvStatus.Ready or MvStatus.Active)) ||
                row.AllowedStatuses.Distinct().Count() != row.AllowedStatuses.Count)
            {
                return MvActivationResult.Rejected(
                    MvActivationFailureReason.UnsafeLifecycle,
                    "Serving-status restore accepts only CatchingUp, Ready, or Active expected statuses.");
            }

            try
            {
                var minimumCurrent = MvCheckpointTruthCodec.Decode(row.MinimumCurrentCheckpointTruth);
                var target = MvCheckpointTruthCodec.Decode(row.ExpectedTargetCheckpointTruth);
                if (!minimumCurrent.IsKnown || minimumCurrent.Provenance is null ||
                    minimumCurrent.Provenance.Kind == MvCheckpointProvenanceKind.LegacyCompatibility)
                {
                    return MvActivationResult.Rejected(
                        MvActivationFailureReason.CurrentCheckpointUnknown,
                        "Serving-status restore requires a Known non-legacy minimum current checkpoint.");
                }

                if (!target.IsKnown || target.Provenance?.Kind != MvCheckpointProvenanceKind.AuthoritativeTargetCapture)
                {
                    return MvActivationResult.Rejected(
                        MvActivationFailureReason.TargetUnknown,
                        "Serving-status restore requires an authoritative Known target checkpoint.");
                }
            }
            catch (MvCheckpointMalformedException ex)
            {
                return MvActivationResult.Rejected(MvActivationFailureReason.ProviderFailure, ex.Message);
            }
        }

        return null;
    }
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

        var switchKind = MvSwitchKind.Initial;
        if (active is not null)
        {
            switchKind = viewVersion > active.ActiveVersion ? MvSwitchKind.Forward : MvSwitchKind.Reverse;
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
            expectedTargetTruth)
        {
            SwitchKind = switchKind
        };
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
