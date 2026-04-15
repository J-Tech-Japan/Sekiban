using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sekiban.Dcb.MaterializedView;

public sealed class MvCatchUpWorker : BackgroundService
{
    private readonly IMvExecutor _executor;
    private readonly ILogger<MvCatchUpWorker> _logger;
    private readonly IReadOnlyList<IMaterializedViewProjector> _projectors;
    private readonly MvWorkerOptions _options;

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
            foreach (var projector in _projectors)
            {
                try
                {
                    var result = await _executor.CatchUpOnceAsync(projector, stoppingToken).ConfigureAwait(false);
                    processedEvents += result.AppliedEvents;
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
                    _logger.LogError(
                        ex,
                        "Materialized view worker halted on {ViewName}/{ViewVersion}.",
                        projector.ViewName,
                        projector.ViewVersion);
                    return;
                }
            }

            if (processedEvents == 0)
            {
                await Task.Delay(_options.PollInterval, stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
