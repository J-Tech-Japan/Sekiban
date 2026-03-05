using Sekiban.Dcb.ColdEvents;
using Sekiban.Dcb.ServiceId;

namespace DcbOrleans.Catchup.Functions;

public sealed class ColdExportTimerService : BackgroundService
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultCycleBudget = TimeSpan.FromMinutes(3);

    private readonly IColdEventExporter _exporter;
    private readonly IServiceIdProvider _serviceIdProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ColdExportTimerService> _logger;

    public ColdExportTimerService(
        IColdEventExporter exporter,
        IServiceIdProvider serviceIdProvider,
        IConfiguration configuration,
        ILogger<ColdExportTimerService> logger)
    {
        _exporter = exporter;
        _serviceIdProvider = serviceIdProvider;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = ResolveInterval();
        var cycleBudget = ResolveCycleBudget();
        var serviceId = _serviceIdProvider.GetCurrentServiceId();

        _logger.LogInformation(
            "Cold export timer started. ServiceId={ServiceId}, Interval={Interval}, CycleBudget={CycleBudget}",
            serviceId,
            interval,
            cycleBudget);

        using var timer = new PeriodicTimer(interval);

        await RunCycleAsync(serviceId, cycleBudget, stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunCycleAsync(serviceId, cycleBudget, stoppingToken);
        }
    }

    private async Task RunCycleAsync(
        string serviceId,
        TimeSpan cycleBudget,
        CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        if (cycleBudget <= TimeSpan.Zero)
        {
            _logger.LogWarning("Cold export cycle skipped because CycleBudget is non-positive: {CycleBudget}", cycleBudget);
            return;
        }

        using var cycleCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        cycleCts.CancelAfter(cycleBudget);

        try
        {
            var result = await _exporter.ExportIncrementalAsync(serviceId, cycleCts.Token);
            if (result.IsSuccess)
            {
                var value = result.GetValue();
                _logger.LogInformation(
                    "Cold export cycle finished. Success=True, ExportedEvents={ExportedEvents}, NewSegments={NewSegments}, Reason={Reason}, CycleBudget={CycleBudget}",
                    value.ExportedEventCount,
                    value.NewSegments.Count,
                    value.Reason,
                    cycleBudget);
                return;
            }

            _logger.LogWarning(
                result.GetException(),
                "Cold export cycle finished. Success=False, CycleBudget={CycleBudget}",
                cycleBudget);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Cold export cycle timed out after {CycleBudget}. Processing will continue on next interval.",
                cycleBudget);
        }
    }

    private TimeSpan ResolveInterval()
    {
        var raw = _configuration["ColdExport:Interval"];
        if (!string.IsNullOrWhiteSpace(raw) && TimeSpan.TryParse(raw, out var parsed) && parsed > TimeSpan.Zero)
        {
            return parsed;
        }

        return DefaultInterval;
    }

    private TimeSpan ResolveCycleBudget()
    {
        var raw = _configuration["ColdExport:CycleBudget"];
        if (!string.IsNullOrWhiteSpace(raw) && TimeSpan.TryParse(raw, out var parsed) && parsed > TimeSpan.Zero)
        {
            return parsed;
        }

        return DefaultCycleBudget;
    }
}
