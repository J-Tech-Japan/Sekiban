using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Sekiban.Dcb.ColdEvents;

namespace DcbOrleans.Catchup.Functions;

public sealed class ColdExportTimerFunction
{
    private readonly ColdExportCycleRunner _runner;
    private readonly ILogger<ColdExportTimerFunction> _logger;

    public ColdExportTimerFunction(
        ColdExportCycleRunner runner,
        ILogger<ColdExportTimerFunction> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    [Function(nameof(ColdExportTimerFunction))]
    public async Task RunAsync(
        [TimerTrigger("%ColdExportTimerSchedule%", UseMonitor = true)] TimerInfo timerInfo,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Cold export timer triggered at {TriggeredAtUtc}. IsPastDue={IsPastDue}, Next={NextScheduleUtc}",
            DateTimeOffset.UtcNow,
            timerInfo.IsPastDue,
            timerInfo.ScheduleStatus?.Next);

        await _runner.RunExportCycleWithBudgetAsync(cancellationToken);
    }
}
