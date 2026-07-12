using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.CosmosDb.Repair;
namespace Sekiban.Dcb.CosmosDb.Sweep;

/// <summary>
///     Runs one repair pass for one lineage. Behind a seam so the sweep's scheduling — jitter, budget,
///     interval, checkpointing, failure handling — can be tested without Cosmos.
/// </summary>
internal interface ITagRepairRunner
{
    Task<CosmosTagRepairReport> RunAsync(
        string serviceId,
        CosmosTagRepairOptions options,
        CancellationToken cancellationToken);
}

/// <summary>
///     Clock, delay, and jitter for the sweep. Substituted in tests so a run that would take an hour of
///     wall-clock takes none.
/// </summary>
internal interface ISweepClock
{
    DateTime UtcNow { get; }

    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);

    /// <summary>A value in [0, 1) used to spread replicas' startup sweeps apart.</summary>
    double NextJitter();
}

internal sealed class SystemSweepClock : ISweepClock
{
    public static readonly SystemSweepClock Instance = new();

    public DateTime UtcNow => DateTime.UtcNow;

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);

    // Jitter only spreads replicas apart; it guards nothing, so a fast PRNG is the right tool.
#pragma warning disable CA5394
    public double NextJitter() => Random.Shared.NextDouble();
#pragma warning restore CA5394
}

/// <summary>
///     Automatically repairs recent tag-index residue.
///     A crash between the two phases of a Cosmos write leaves a durable event whose tag rows never landed.
///     The repair service can fix that, but only when an operator runs it — so routine residue sits
///     unrepaired until somebody notices. This sweep closes that gap: it runs the repair over a recent
///     window shortly after startup, and optionally on an interval.
///     What it does NOT do is make tag reads safe. It is eventual repair, not a readiness gate: it does not
///     block or gate tag readers, a missing-tag window remains open until a run reaches it, and
///     <c>GeneralTagConsistentActor</c>'s optimistic-concurrency baseline can be regressed for that whole
///     window. See the storage-provider docs — the non-guarantee is stated there deliberately, rather than
///     letting an automatic sweep imply a safety it cannot provide.
///     It is non-destructive for the same reason the repair service is: it can only reach that service's
///     surface, which creates missing rows and classifies everything else. No configuration makes it delete,
///     rewrite, canonicalize, or de-duplicate a row — those have no code path from here.
///     Disabled by default. Referencing the package starts nothing.
/// </summary>
public sealed class CosmosTagSweepService : IHostedService, IDisposable
{
    private readonly ISweepClock _clock;
    private readonly ILogger<CosmosTagSweepService>? _logger;
    private readonly CosmosTagSweepOptions _options;
    private readonly ITagRepairRunner _runner;
    private readonly IReadOnlyList<string> _serviceIds;

    /// <summary>Per-lineage resume point, carried across runs within this process.</summary>
    private readonly Dictionary<string, string?> _checkpoints = new(StringComparer.Ordinal);

    private CancellationTokenSource? _stopping;
    private Task? _sweeping;

    /// <summary>
    ///     The background sweep, so tests can await a cycle instead of racing it. Null until started.
    /// </summary>
    internal Task? Sweeping => _sweeping;

    internal CosmosTagSweepService(
        CosmosTagSweepOptions options,
        IReadOnlyList<string> serviceIds,
        ITagRepairRunner runner,
        ISweepClock? clock = null,
        ILogger<CosmosTagSweepService>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _serviceIds = serviceIds ?? throw new ArgumentNullException(nameof(serviceIds));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _clock = clock ?? SystemSweepClock.Instance;
        _logger = logger;
    }

    /// <summary>
    ///     Starts the sweep in the background. Startup is never blocked: this returns immediately, and the
    ///     sweep's own budget bounds how long any run may take.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return Task.CompletedTask;
        }

        _stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _sweeping = Task.Run(() => SweepLoopAsync(_stopping.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Cancels the sweep and waits briefly for it to unwind.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_stopping == null || _sweeping == null)
        {
            return;
        }

        await _stopping.CancelAsync().ConfigureAwait(false);

        try
        {
            await _sweeping.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down; the run does not get to hold up the host.
        }
    }

    /// <inheritdoc />
    public void Dispose() => _stopping?.Dispose();

    private async Task SweepLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_options.RunOnStartup)
            {
                await ApplyStartupJitterAsync(cancellationToken).ConfigureAwait(false);
                await SweepAllLineagesAsync(cancellationToken).ConfigureAwait(false);
            }

            while (_options.Interval is { } interval && interval > TimeSpan.Zero)
            {
                await _clock.DelayAsync(interval, cancellationToken).ConfigureAwait(false);
                await SweepAllLineagesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown. Nothing to repair about that.
        }
    }

    /// <summary>
    ///     Replicas of a service start at roughly the same moment. Without this they would all sweep at once
    ///     and spike RU together.
    /// </summary>
    private async Task ApplyStartupJitterAsync(CancellationToken cancellationToken)
    {
        if (_options.MaxStartupJitter <= TimeSpan.Zero)
        {
            return;
        }

        var jitter = TimeSpan.FromTicks((long)(_options.MaxStartupJitter.Ticks * _clock.NextJitter()));
        await _clock.DelayAsync(jitter, cancellationToken).ConfigureAwait(false);
    }

    private async Task SweepAllLineagesAsync(CancellationToken cancellationToken)
    {
        foreach (var serviceId in _serviceIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await SweepLineageAsync(serviceId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SweepLineageAsync(string serviceId, CancellationToken cancellationToken)
    {
        // One run gets a wall-clock budget. If it runs out, the run stops and its checkpoint carries the
        // rest to the next turn — a sweep that overruns must not become a sweep that never stops.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_options.RunBudget > TimeSpan.Zero)
        {
            budget.CancelAfter(_options.RunBudget);
        }

        var options = BuildRepairOptions(serviceId);

        try
        {
            var report = await _runner.RunAsync(serviceId, options, budget.Token).ConfigureAwait(false);

            // Resume where this run stopped; start a fresh window next time it completed the range.
            _checkpoints[serviceId] = report.HasMore ? report.Checkpoint : null;

            CosmosDbTelemetry.RecordSweepRun(SweepRunOutcome.Completed);
            CosmosDbTelemetry.RecordSweepRepairedRows(report.Repaired);

            if (_logger != null)
                LogSweepCompleted(
                    _logger,
                    serviceId,
                    report.EventsScanned,
                    report.Repaired,
                    $"legacy-present {report.LegacyPresent}, duplicate {report.Duplicate}, " +
                    $"corrupt {report.Corrupt}, overflow {report.Overflow}, RU {report.RequestCharge:F1}",
                    null);

            if (report.Corrupt > 0 || report.Overflow > 0)
            {
                // Surfaced, never acted on: repairing these would mean rewriting or removing rows, which is
                // exactly what this sweep must not do.
                CosmosDbTelemetry.RecordSweepAttention(report.Corrupt, report.Overflow);

                if (_logger != null)
                    LogSweepNeedsAttention(_logger, serviceId, report.Corrupt, report.Overflow, null);
            }
        }
        // Shutdown first: the host going away is not a resumable budget overrun, and must not be recorded as
        // one. Nothing is persisted — the sweep simply stops.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CosmosTagRepairCancelledException ex)
        {
            // The run's own budget elapsed. Expected under load — and the events it settled before the budget
            // ran out are real progress, so its checkpoint is persisted. Without this, a budget too tight to
            // finish the window would re-scan the same prefix every turn and never advance.
            _checkpoints[serviceId] = ex.PartialReport.Checkpoint;

            CosmosDbTelemetry.RecordSweepRun(SweepRunOutcome.BudgetExhausted);
            CosmosDbTelemetry.RecordSweepRepairedRows(ex.PartialReport.Repaired);

            if (_logger != null)
                LogSweepBudgetExhausted(
                    _logger,
                    serviceId,
                    _options.RunBudget.TotalSeconds,
                    ex.PartialReport.EventsScanned,
                    ex.PartialReport.Repaired,
                    null);
        }
        catch (OperationCanceledException)
        {
            // The budget elapsed somewhere that could not report progress. Nothing to persist; the next run
            // re-scans from the same place, which is safe because repair is idempotent.
            CosmosDbTelemetry.RecordSweepRun(SweepRunOutcome.BudgetExhausted);

            if (_logger != null)
                LogSweepBudgetExhausted(_logger, serviceId, _options.RunBudget.TotalSeconds, 0, 0, null);
        }
#pragma warning disable CA1031 // A failed sweep is logged and retried next turn — it must never take the host down.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            CosmosDbTelemetry.RecordSweepRun(SweepRunOutcome.Failed);

            if (_logger != null)
                LogSweepFailed(_logger, serviceId, ex);
        }
    }

    private CosmosTagRepairOptions BuildRepairOptions(string serviceId)
    {
        var checkpoint = _checkpoints.GetValueOrDefault(serviceId);

        return new CosmosTagRepairOptions
        {
            // Not a dry run — but "not a dry run" only ever means "create the rows that are missing".
            DryRun = false,
            FromSortableUniqueIdExclusive = checkpoint == null ? WindowStart() : null,
            Checkpoint = checkpoint,
            MaxEventsToScan = _options.MaxEventsPerRun,
            MaxParallelism = _options.MaxParallelism
        };
    }

    /// <summary>
    ///     The lower bound of the recent window, as a sortableUniqueId. Crash residue is recent, so the sweep
    ///     does not re-read history it has no reason to doubt.
    /// </summary>
    private string WindowStart() =>
        SortableUniqueId.Generate(_clock.UtcNow - _options.Window, Guid.Empty);

    private static readonly Action<ILogger, string, int, int, string, Exception?> LogSweepCompleted =
        LoggerMessage.Define<string, int, int, string>(
            LogLevel.Information,
            new EventId(1, nameof(LogSweepCompleted)),
            "Tag sweep completed for service '{ServiceId}': scanned {EventsScanned} events, repaired {Repaired}. {Classification}");

    private static readonly Action<ILogger, string, int, int, Exception?> LogSweepNeedsAttention =
        LoggerMessage.Define<string, int, int>(
            LogLevel.Warning,
            new EventId(2, nameof(LogSweepNeedsAttention)),
            "Tag sweep for service '{ServiceId}' found {Corrupt} corrupt and {Overflow} overflowed key(s). " +
            "These are reported only — the sweep never rewrites or removes a row. Investigate with a manual dry run.");

    private static readonly Action<ILogger, string, double, int, int, Exception?> LogSweepBudgetExhausted =
        LoggerMessage.Define<string, double, int, int>(
            LogLevel.Information,
            new EventId(3, nameof(LogSweepBudgetExhausted)),
            "Tag sweep for service '{ServiceId}' hit its {BudgetSeconds}s run budget after settling " +
            "{EventsScanned} event(s) and repairing {Repaired}; it resumes from there next turn");

    private static readonly Action<ILogger, string, Exception?> LogSweepFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(4, nameof(LogSweepFailed)),
            "Tag sweep failed for service '{ServiceId}'. The host is unaffected; the next run will retry.");
}
