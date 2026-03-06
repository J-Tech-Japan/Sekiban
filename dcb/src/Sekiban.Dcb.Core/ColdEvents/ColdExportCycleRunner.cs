using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.ServiceId;

namespace Sekiban.Dcb.ColdEvents;

public sealed class ColdExportCycleRunner
{
    private readonly IColdEventExporter _exporter;
    private readonly ColdEventStoreOptions _options;
    private readonly IServiceIdProvider _serviceIdProvider;
    private readonly ILogger<ColdExportCycleRunner> _logger;

    public ColdExportCycleRunner(
        IColdEventExporter exporter,
        IOptions<ColdEventStoreOptions> options,
        IServiceIdProvider serviceIdProvider,
        ILogger<ColdExportCycleRunner> logger)
    {
        _exporter = exporter;
        _options = options.Value;
        _serviceIdProvider = serviceIdProvider;
        _logger = logger;
    }

    public bool IsEnabled => _options.Enabled;

    public TimeSpan PullInterval => _options.PullInterval;

    public bool RunOnStartup => _options.RunOnStartup;

    public TimeSpan? CycleBudget => _options.ExportCycleBudget;

    public async Task RunExportCycleWithBudgetAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Cold event export is disabled, skipping cycle");
            return;
        }

        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        if (_options.ExportCycleBudget is { } configuredBudget && configuredBudget <= TimeSpan.Zero)
        {
            _logger.LogWarning(
                "Cold event export cycle skipped because CycleBudget is non-positive: {CycleBudget}",
                configuredBudget);
            return;
        }

        using var cycleCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        if (_options.ExportCycleBudget is { } positiveBudget)
        {
            cycleCts.CancelAfter(positiveBudget);
        }

        try
        {
            await RunExportCycleAsync(cycleCts.Token);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug("Cold event export cycle cancelled by host shutdown.");
        }
        catch (OperationCanceledException) when (_options.ExportCycleBudget is { } timedBudget)
        {
            _logger.LogWarning(
                "Cold event export cycle timed out after {CycleBudget}. Processing will continue on next trigger.",
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
            "Cold event export completed for {ServiceId}: exported {Count} events, {SegmentCount} written/updated segments, reason={Reason}",
            serviceId,
            value.ExportedEventCount,
            value.NewSegments.Count,
            value.Reason);
    }
}
