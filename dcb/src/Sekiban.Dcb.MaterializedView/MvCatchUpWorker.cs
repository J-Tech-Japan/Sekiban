using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sekiban.Dcb.MaterializedView;

public sealed class MvCatchUpWorker : BackgroundService
{
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
        foreach (var projector in _projectors)
        {
            await _executor.InitializeAsync(projector, stoppingToken).ConfigureAwait(false);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var processedEvents = 0;
            var shouldDelay = false;
            foreach (var projector in _projectors)
            {
                try
                {
                    var result = await _executor.CatchUpOnceAsync(projector, stoppingToken).ConfigureAwait(false);
                    processedEvents += result.AppliedEvents;
                    shouldDelay |= result.ReachedUnsafeWindow;
                    _failureCounts.Remove(GetProjectorKey(projector));
                }
                catch (NotSupportedException ex)
                {
                    _logger.LogError(
                        ex,
                        "Materialized view worker stopped because the configured event store cannot stream all events for {ViewName}/{ViewVersion}.",
                        projector.ViewName,
                        projector.ViewVersion);
                    return;
                }
                catch (Exception ex)
                {
                    var key = GetProjectorKey(projector);
                    var failures = _failureCounts.TryGetValue(key, out var currentFailures) ? currentFailures + 1 : 1;
                    _failureCounts[key] = failures;

                    if (failures >= _options.MaxConsecutiveFailuresBeforeStop)
                    {
                        _logger.LogError(
                            ex,
                            "Materialized view worker halted on {ViewName}/{ViewVersion} after {FailureCount} consecutive failures.",
                            projector.ViewName,
                            projector.ViewVersion,
                            failures);
                        return;
                    }

                    _logger.LogWarning(
                        ex,
                        "Materialized view worker retrying {ViewName}/{ViewVersion} after failure {FailureCount}/{MaxFailures}.",
                        projector.ViewName,
                        projector.ViewVersion,
                        failures,
                        _options.MaxConsecutiveFailuresBeforeStop);
                    shouldDelay = true;
                }
            }

            if (processedEvents == 0 || shouldDelay)
            {
                await Task.Delay(_options.PollInterval, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private readonly Dictionary<string, int> _failureCounts = new(StringComparer.Ordinal);

    private static string GetProjectorKey(IMaterializedViewProjector projector) =>
        $"{projector.ViewName}:{projector.ViewVersion}";
}
