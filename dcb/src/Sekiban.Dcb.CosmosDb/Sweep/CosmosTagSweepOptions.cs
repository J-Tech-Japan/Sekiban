namespace Sekiban.Dcb.CosmosDb.Sweep;

/// <summary>
///     Configuration for the automatic tag-index sweep.
///     Disabled by default, and there is deliberately no setting here that can make the sweep destructive: it
///     runs the repair service's non-destructive surface — backfill a missing row, classify everything else —
///     and nothing in this type can widen that. Deleting, rewriting, or de-duplicating a legacy row is a
///     separate, separately-authorized concern that the sweep has no code path to.
/// </summary>
public class CosmosTagSweepOptions
{
    /// <summary>
    ///     Whether the sweep runs at all. Default: <c>false</c>.
    ///     Referencing or upgrading the package must not start scanning anybody's containers, so this stays
    ///     off until someone turns it on.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    ///     How far back from now each run scans. Crash residue is recent by nature, so the sweep looks at a
    ///     recent window rather than the whole history — a full backfill is a manual repair job.
    ///     Default: 24 hours.
    /// </summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    ///     Whether to run once shortly after startup. Default: true (when <see cref="Enabled" />).
    /// </summary>
    public bool RunOnStartup { get; set; } = true;

    /// <summary>
    ///     How often to run after the startup run. Null runs only at startup. Default: null.
    ///     Measure the RU cost of a manual dry run before turning this on.
    /// </summary>
    public TimeSpan? Interval { get; set; }

    /// <summary>
    ///     Wall-clock bound on a single run. When it elapses the run is cancelled and the sweep waits for its
    ///     next turn; the events it did not reach are picked up then, from the checkpoint.
    ///     Default: 5 minutes.
    /// </summary>
    public TimeSpan RunBudget { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Event documents a single run may examine. Bounds one run's RU cost. Default: 10,000.
    /// </summary>
    public int MaxEventsPerRun { get; set; } = 10_000;

    /// <summary>
    ///     Keys classified concurrently. This is the RU-rate dial: a background sweep should yield to live
    ///     traffic, so keep it low. Default: 2.
    /// </summary>
    public int MaxParallelism { get; set; } = 2;

    /// <summary>
    ///     Upper bound on the random delay before the startup run. Replicas of the same service start at
    ///     roughly the same moment, so without jitter they would all sweep at once and spike RU together.
    ///     Default: 30 seconds.
    /// </summary>
    public TimeSpan MaxStartupJitter { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     The lineages to sweep. Each entry is swept independently, with its own options-derived window and
    ///     its own checkpoint. Empty means the host's current service id — the right thing for a
    ///     single-tenant deployment.
    /// </summary>
    public IList<string> ServiceIds { get; } = new List<string>();
}
