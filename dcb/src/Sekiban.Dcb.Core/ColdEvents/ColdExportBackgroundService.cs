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

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunExportCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in cold event export cycle");
            }

            await Task.Delay(_options.PullInterval, stoppingToken);
        }
    }

    private async Task RunExportCycleAsync(CancellationToken ct)
    {
        var serviceId = _serviceIdProvider.GetCurrentServiceId();
        _logger.LogInformation("Starting cold event export cycle for {ServiceId}", serviceId);

        var result = await _exporter.ExportIncrementalAsync(serviceId, ct);

        if (result.IsSuccess)
        {
            var value = result.GetValue();
            _logger.LogInformation(
                "Cold event export completed for {ServiceId}: exported {Count} events, {SegmentCount} new segments",
                serviceId, value.ExportedEventCount, value.NewSegments.Count);
        }
        else
        {
            _logger.LogError(
                result.GetException(),
                "Cold event export failed for {ServiceId}",
                serviceId);
        }
    }
}
