using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sekiban.Dcb.MaterializedView;

public sealed class MvCatchUpWorker : BackgroundService
{
    private readonly Dictionary<string, int> _failureCounts = new(StringComparer.Ordinal);
    private readonly IMvExecutor _executor;
    private readonly IMvApplyHostFactory _hostFactory;
    private readonly ILogger<MvCatchUpWorker> _logger;
    private readonly IReadOnlyList<MvApplyHostRegistration> _registrations;
    private readonly MvOptions _options;

    public MvCatchUpWorker(
        IMvApplyHostFactory hostFactory,
        IMvExecutor executor,
        IOptions<MvOptions> options,
        ILogger<MvCatchUpWorker> logger)
    {
        _executor = executor;
        _hostFactory = hostFactory;
        _logger = logger;
        _registrations = hostFactory.GetRegistrations();
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
        foreach (var registration in _registrations)
        {
            var host = _hostFactory.Create(registration.ViewName, registration.ViewVersion);
            await _executor.InitializeAsync(host, cancellationToken: stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task<CatchUpCycleResult> RunCatchUpCycleAsync(CancellationToken stoppingToken)
    {
        var appliedEvents = 0;
        var shouldDelay = false;

        foreach (var registration in _registrations)
        {
            var projectorResult = await ProcessProjectorAsync(registration, stoppingToken).ConfigureAwait(false);
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
        MvApplyHostRegistration registration,
        CancellationToken stoppingToken)
    {
        var host = _hostFactory.Create(registration.ViewName, registration.ViewVersion);
        try
        {
            var result = await _executor.CatchUpOnceAsync(host, cancellationToken: stoppingToken).ConfigureAwait(false);
            _failureCounts.Remove(GetProjectorKey(registration));
            return new CatchUpCycleResult(result.AppliedEvents, result.ReachedUnsafeWindow, ShouldStop: false);
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(
                ex,
                "Materialized view worker stopped because the configured event store cannot stream all events for {ViewName}/{ViewVersion}.",
                registration.ViewName,
                registration.ViewVersion);
            return new CatchUpCycleResult(0, ShouldDelay: false, ShouldStop: true);
        }
        catch (Exception ex)
        {
            var failures = IncrementFailureCount(registration);
            if (failures >= _options.MaxConsecutiveFailuresBeforeStop)
            {
                _logger.LogError(
                    ex,
                    "Materialized view worker halted on {ViewName}/{ViewVersion} after {FailureCount} consecutive failures.",
                    registration.ViewName,
                    registration.ViewVersion,
                    failures);
                return new CatchUpCycleResult(0, ShouldDelay: false, ShouldStop: true);
            }

            _logger.LogWarning(
                ex,
                "Materialized view worker retrying {ViewName}/{ViewVersion} after failure {FailureCount}/{MaxFailures}.",
                registration.ViewName,
                registration.ViewVersion,
                failures,
                _options.MaxConsecutiveFailuresBeforeStop);
            return new CatchUpCycleResult(0, ShouldDelay: true, ShouldStop: false);
        }
    }

    private int IncrementFailureCount(MvApplyHostRegistration registration)
    {
        var key = GetProjectorKey(registration);
        var failures = _failureCounts.TryGetValue(key, out var currentFailures) ? currentFailures + 1 : 1;
        _failureCounts[key] = failures;
        return failures;
    }

    private static string GetProjectorKey(MvApplyHostRegistration registration) =>
        $"{registration.ViewName}:{registration.ViewVersion}";

    private sealed record CatchUpCycleResult(int AppliedEvents, bool ShouldDelay, bool ShouldStop);
}
