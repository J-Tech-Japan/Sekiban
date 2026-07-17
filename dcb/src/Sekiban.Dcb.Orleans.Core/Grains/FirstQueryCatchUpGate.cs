namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     The fresh-activation first-query barrier as a standalone, thread-safe single-flight gate. When armed (a fresh
///     activation restored no fault descriptor), the FIRST query runs the catch-up work exactly once; concurrent
///     callers share that one in-flight task and all observe its outcome — none bypasses while it runs.
///     On success the gate is satisfied and later calls are no-ops (ordinary lag semantics resume). On failure (the
///     work throws — a head/read/catch-up failure) the gate is NOT satisfied and the in-flight handle is dropped, so
///     the next call retries; but every failed caller still observes the throw. No caller succeeds because a flag was
///     cleared early. It is framework-free so a friend test can drive it directly with a parked work task and real
///     concurrent callers — not relying on Orleans non-reentrancy for the single-flight guarantee.
/// </summary>
internal sealed class FirstQueryCatchUpGate
{
    private readonly object _sync = new();
    private bool _armed;
    private bool _satisfied;
    private Task? _inFlight;

    /// <summary>Arm the gate for a fresh activation that restored no fault descriptor.</summary>
    public void Arm()
    {
        lock (_sync)
        {
            _armed = true;
        }
    }

    /// <summary>True while the gate is armed and not yet satisfied — the first query must run the barrier.</summary>
    public bool IsPending
    {
        get
        {
            lock (_sync)
            {
                return _armed && !_satisfied;
            }
        }
    }

    /// <summary>
    ///     Runs <paramref name="work" /> at most once across all concurrent callers while it is in flight. A no-op
    ///     (completed task) when the gate is not armed or already satisfied. If the previous attempt has completed but
    ///     failed (the gate is not satisfied), a fresh attempt is started — so a transient failure is retried on the
    ///     next call, while callers that arrive DURING a run share the one in-flight task and observe its outcome.
    /// </summary>
    public Task EnsureAsync(Func<Task> work)
    {
        lock (_sync)
        {
            if (!_armed || _satisfied)
            {
                return Task.CompletedTask;
            }

            // Share the run only while it is genuinely in flight. A completed-but-faulted handle means the previous
            // attempt failed (success would have set _satisfied): start a fresh attempt rather than replay the old
            // failure forever. (Clearing _inFlight from SettleAsync's catch would race the assignment below when the
            // work completes synchronously, leaving a stale faulted handle — hence this completion check instead.)
            if (_inFlight is { IsCompleted: false })
            {
                return _inFlight;
            }

            _inFlight = SettleAsync(work);
            return _inFlight;
        }
    }

    private async Task SettleAsync(Func<Task> work)
    {
        await work(); // throws on failure -> this task faults, _satisfied stays false, so the next call retries
        lock (_sync)
        {
            _satisfied = true;
        }
    }
}
