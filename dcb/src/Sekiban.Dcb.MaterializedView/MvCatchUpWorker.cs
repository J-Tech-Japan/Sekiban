using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sekiban.Dcb.MaterializedView;

public sealed class MvCatchUpWorker : BackgroundService
{
    private readonly Dictionary<string, int> _failureCounts = new(StringComparer.Ordinal);
    private readonly IMvExecutor _executor;
    private readonly ILogger<MvCatchUpWorker> _logger;
    private readonly IReadOnlyList<IMaterializedViewProjector> _projectors;
    private readonly MvOptions _options;

    public MvCatchUpWorker(
        IEnumerable<IMaterializedViewProjector> projectors,
        IMvExecutor executor,
        IOptions<MvOptions> options,
        ILogger<MvCatchUpWorker> logger)
    {
        _executor = executor;
        _logger = logger;
        _projectors = projectors.ToList();
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await InitializeProjectorsAsync(stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            var cycle = await RunCatchUpCycleAsync(stoppingToken).ConfigureAwait(false);
            if (cycle.ShouldStop)
            {
                return;
            }

            if (cycle.AppliedEvents == 0 || cycle.ShouldDelay)
            {
                await Task.Delay(_options.PollInterval, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task InitializeProjectorsAsync(CancellationToken stoppingToken)
    {
        foreach (var projector in _projectors)
        {
            await _executor.InitializeAsync(projector, cancellationToken: stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task<CatchUpCycleResult> RunCatchUpCycleAsync(CancellationToken stoppingToken)
    {
        var appliedEvents = 0;
        var shouldDelay = false;

        foreach (var projector in _projectors)
        {
            var projectorResult = await ProcessProjectorAsync(projector, stoppingToken).ConfigureAwait(false);
            if (projectorResult.ShouldStop)
            {
                return projectorResult;
            }

            appliedEvents += projectorResult.AppliedEvents;
            shouldDelay |= projectorResult.ShouldDelay;
        }

        return new CatchUpCycleResult(appliedEvents, shouldDelay, ShouldStop: false);
    }

    private async Task<CatchUpCycleResult> ProcessProjectorAsync(
        IMaterializedViewProjector projector,
        CancellationToken stoppingToken)
    {
        try
        {
            var result = await _executor.CatchUpOnceAsync(projector, cancellationToken: stoppingToken).ConfigureAwait(false);
            _failureCounts.Remove(GetProjectorKey(projector));
            return new CatchUpCycleResult(result.AppliedEvents, result.ReachedUnsafeWindow, ShouldStop: false);
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(
                ex,
                "Materialized view worker stopped because the configured event store cannot stream all events for {ViewName}/{ViewVersion}.",
                projector.ViewName,
                projector.ViewVersion);
            return new CatchUpCycleResult(0, ShouldDelay: false, ShouldStop: true);
        }
        catch (Exception ex)
        {
            var failures = IncrementFailureCount(projector);
            if (failures >= _options.MaxConsecutiveFailuresBeforeStop)
            {
                _logger.LogError(
                    ex,
                    "Materialized view worker halted on {ViewName}/{ViewVersion} after {FailureCount} consecutive failures.",
                    projector.ViewName,
                    projector.ViewVersion,
                    failures);
                return new CatchUpCycleResult(0, ShouldDelay: false, ShouldStop: true);
            }

            _logger.LogWarning(
                ex,
                "Materialized view worker retrying {ViewName}/{ViewVersion} after failure {FailureCount}/{MaxFailures}.",
                projector.ViewName,
                projector.ViewVersion,
                failures,
                _options.MaxConsecutiveFailuresBeforeStop);
            return new CatchUpCycleResult(0, ShouldDelay: true, ShouldStop: false);
        }
    }

    private int IncrementFailureCount(IMaterializedViewProjector projector)
    {
        var key = GetProjectorKey(projector);
        var failures = _failureCounts.TryGetValue(key, out var currentFailures) ? currentFailures + 1 : 1;
        _failureCounts[key] = failures;
        return failures;
    }

    private static string GetProjectorKey(IMaterializedViewProjector projector) =>
        $"{projector.ViewName}:{projector.ViewVersion}";

    private sealed record CatchUpCycleResult(int AppliedEvents, bool ShouldDelay, bool ShouldStop);
}
