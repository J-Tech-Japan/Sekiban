using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.ServiceId;
namespace Sekiban.Dcb.ColdEvents;

public sealed class ColdExportBackgroundService : BackgroundService
{
    private readonly IColdEventExporter _exporter;
    private readonly ColdEventStoreOptions _options;
    private readonly IServiceIdProvider _serviceIdProvider;
    private readonly ILogger<ColdExportBackgroundService> _logger;

    public ColdExportBackgroundService(
        IColdEventExporter exporter,
        IOptions<ColdEventStoreOptions> options,
        IServiceIdProvider serviceIdProvider,
        ILogger<ColdExportBackgroundService> logger)
    {
        _exporter = exporter;
        _options = options.Value;
        _serviceIdProvider = serviceIdProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Cold event export is disabled, background service will not run");
            return;
        }

        var cycleBudget = _options.ExportCycleBudget;
        _logger.LogInformation(
            "Cold event export background service started. Interval={PullInterval}, CycleBudget={CycleBudget}",
            _options.PullInterval,
            cycleBudget);

        using var timer = new PeriodicTimer(_options.PullInterval);

        if (_options.RunOnStartup)
        {
            await RunExportCycleWithBudgetAsync(cycleBudget, stoppingToken);
        }

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunExportCycleWithBudgetAsync(cycleBudget, stoppingToken);
        }
    }

    private async Task RunExportCycleWithBudgetAsync(
        TimeSpan? cycleBudget,
        CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        if (cycleBudget is { } configuredBudget && configuredBudget <= TimeSpan.Zero)
        {
            _logger.LogWarning(
                "Cold event export cycle skipped because CycleBudget is non-positive: {CycleBudget}",
                configuredBudget);
            return;
        }

        using var cycleCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        if (cycleBudget is { } positiveBudget)
        {
            cycleCts.CancelAfter(positiveBudget);
        }

        try
        {
            await RunExportCycleAsync(cycleCts.Token);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (OperationCanceledException) when (cycleBudget is { } timedBudget)
        {
            _logger.LogWarning(
                "Cold event export cycle timed out after {CycleBudget}. Processing will continue on next interval.",
                timedBudget);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in cold event export cycle");
        }
    }

    private async Task RunExportCycleAsync(CancellationToken ct)
    {
        var serviceId = _serviceIdProvider.GetCurrentServiceId();
        _logger.LogInformation("Starting cold event export cycle for {ServiceId}", serviceId);

        var result = await _exporter.ExportIncrementalAsync(serviceId, ct);

        if (!result.IsSuccess)
        {
            _logger.LogError(result.GetException(), "Cold event export failed for {ServiceId}", serviceId);
            return;
        }

        var value = result.GetValue();
        _logger.LogInformation(
            "Cold event export completed for {ServiceId}: exported {Count} events, {SegmentCount} written/updated segments",
            serviceId,
            value.ExportedEventCount,
            value.NewSegments.Count);
    }
}
