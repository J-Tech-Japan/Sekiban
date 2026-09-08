using Sekiban.Dcb.Common;
using Sekiban.Dcb.MaterializedView;

namespace Sekiban.Dcb.MaterializedView.Orleans;

/// <summary>
///     Deterministic missing-hint stall accounting for the classic Orleans grain.
///     Only a safe-eligible, newer hint observed with a successful empty/unsafe
///     catch-up result consumes the budget. The clock is injectable so the
///     boundary can be proved without sleeping in a test.
/// </summary>
internal sealed class MvCatchUpStallBudget
{
    private readonly TimeSpan _threshold;
    private readonly Func<DateTimeOffset> _utcNow;
    private string? _hintSortableUniqueId;
    private DateTimeOffset? _firstObservedAtUtc;

    public MvCatchUpStallBudget(TimeSpan threshold, Func<DateTimeOffset>? utcNow = null)
    {
        _threshold = threshold;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public string? HintSortableUniqueId => _hintSortableUniqueId;

    public DateTimeOffset? FirstObservedAtUtc => _firstObservedAtUtc;

    public bool Observe(
        string? hintSortableUniqueId,
        bool newerHintOutstanding,
        int appliedEvents,
        MvCatchUpOutcome outcome,
        bool safeEligible)
    {
        if (!newerHintOutstanding || appliedEvents != 0 || !safeEligible ||
            outcome is not (MvCatchUpOutcome.Empty or MvCatchUpOutcome.UnsafeWindow) ||
            string.IsNullOrWhiteSpace(hintSortableUniqueId))
        {
            return false;
        }

        var now = _utcNow();
        if (!string.Equals(_hintSortableUniqueId, hintSortableUniqueId, StringComparison.Ordinal))
        {
            _hintSortableUniqueId = hintSortableUniqueId;
            _firstObservedAtUtc = now;
            return false;
        }

        _firstObservedAtUtc ??= now;
        return now - _firstObservedAtUtc >= _threshold;
    }

    /// <summary>
    ///     Pauses elapsed-budget accounting after a failed read or retryable failure.
    ///     The hint identity is retained for diagnostics, but the next qualifying
    ///     successful empty/unsafe observation starts a fresh interval.
    /// </summary>
    public void PauseAfterFailure() => _firstObservedAtUtc = null;

    public void Reset()
    {
        _hintSortableUniqueId = null;
        _firstObservedAtUtc = null;
    }

    public static bool IsSafeEligible(
        string? hintSortableUniqueId,
        int safeWindowMs,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(hintSortableUniqueId) ||
            !SortableUniqueId.TryParse(hintSortableUniqueId, out var hint) ||
            hint is null)
        {
            return false;
        }

        var safeThreshold = now.UtcDateTime.AddMilliseconds(-Math.Max(0, safeWindowMs));
        return hint.GetDateTime() <= safeThreshold;
    }
}
