using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace Sekiban.Dcb.ColdEvents;

public sealed class ColdExportBackgroundService : BackgroundService
{
    private readonly ColdExportCycleRunner _runner;
    private readonly ILogger<ColdExportBackgroundService> _logger;

    public ColdExportBackgroundService(
        ColdExportCycleRunner runner,
        ILogger<ColdExportBackgroundService> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_runner.IsEnabled)
        {
            _logger.LogInformation("Cold event export is disabled, background service will not run");
            return;
        }

        _logger.LogInformation(
            "Cold event export background service started. Interval={PullInterval}, CycleBudget={CycleBudget}",
            _runner.PullInterval,
            _runner.CycleBudget);

        using var timer = new PeriodicTimer(_runner.PullInterval);

        if (_runner.RunOnStartup)
        {
            await _runner.RunExportCycleWithBudgetAsync(stoppingToken);
        }

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await _runner.RunExportCycleWithBudgetAsync(stoppingToken);
        }
    }
}
