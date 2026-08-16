using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.ServiceId;

namespace Sekiban.Dcb.MaterializedView;

public sealed class MvCatchUpWorker : BackgroundService
{
    private readonly Dictionary<string, int> _failureCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MvProjectionStatusSnapshot> _publicationSnapshots = new(StringComparer.Ordinal);
    private readonly IMvExecutor _executor;
    private readonly IMvApplyHostFactory _hostFactory;
    private readonly ILogger<MvCatchUpWorker> _logger;
    private readonly IReadOnlyList<MvApplyHostRegistration> _registrations;
    private readonly MvOptions _options;
    private readonly MvProjectionStatusPublisher? _statusPublisher;
    private readonly string _serviceId;

    public MvCatchUpWorker(
        IMvApplyHostFactory hostFactory,
        IMvExecutor executor,
        IOptions<MvOptions> options,
        ILogger<MvCatchUpWorker> logger)
        : this(hostFactory, executor, options, logger, serviceId: null, statusPublisher: null)
    {
    }

    /// <summary>
    /// Creates one immutable worker bound to one exact service identity.
    /// </summary>
    public MvCatchUpWorker(
        IMvApplyHostFactory hostFactory,
        IMvExecutor executor,
        IOptions<MvOptions> options,
        ILogger<MvCatchUpWorker> logger,
        string? serviceId)
        : this(hostFactory, executor, options, logger, serviceId, statusPublisher: null)
    {
    }

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public MvCatchUpWorker(
        IMvApplyHostFactory hostFactory,
        IMvExecutor executor,
        IOptions<MvOptions> options,
        ILogger<MvCatchUpWorker> logger,
        MvProjectionStatusPublisher statusPublisher)
        : this(hostFactory, executor, options, logger, serviceId: null, statusPublisher)
    {
    }

    public MvCatchUpWorker(
        IMvApplyHostFactory hostFactory,
        IMvExecutor executor,
        IOptions<MvOptions> options,
        ILogger<MvCatchUpWorker> logger,
        string? serviceId,
        MvProjectionStatusPublisher? statusPublisher)
    {
        _executor = executor;
        _hostFactory = hostFactory;
        _logger = logger;
        _registrations = hostFactory.GetRegistrations();
        _options = options.Value;
        _serviceId = ResolveWorkerServiceId(serviceId, _options);
        _statusPublisher = statusPublisher;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await InitializeProjectorsAsync(stoppingToken).ConfigureAwait(false);

        var capabilities = MvModeCapabilities.ResolveAndValidate(
            _options,
            MvTransition.CatchUp,
            MvTransitionIdentity.Unknown);
        if (!capabilities.AllowsProjectorApply || !capabilities.AllowsLifecycleDml)
        {
            _logger.LogInformation("Materialized-view worker verified the pre-provisioned contract and will not run a mutating catch-up lifecycle.");
            return;
        }

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
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var registration in _registrations)
                {
                    var host = _hostFactory.Create(registration.ViewName, registration.ViewVersion);
                    await _executor.InitializeAsync(host, _serviceId, stoppingToken).ConfigureAwait(false);
                    _publicationSnapshots[GetProjectorKey(registration)] =
                        MvProjectionStatusSnapshot.Unknown(MvStatus.CatchingUp);
                }

                return;
            }
            catch (MvInitializationException ex) when (
                MvModeCapabilities.ResolveAndValidate(
                        _options,
                        MvTransition.Initialize,
                        MvTransitionIdentity.Unknown)
                    .RequiresVerification &&
                !stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "Verification-mode materialized-view startup is gated until the pre-provisioned schema is compatible; retrying after {RetryDelay}.",
                    GetVerificationRetryDelay());
                foreach (var registration in _registrations)
                {
                    var snapshot = MvProjectionStatusSnapshot.Unknown(MvStatus.Faulted);
                    _publicationSnapshots[GetProjectorKey(registration)] = snapshot;
                    if (_statusPublisher is not null)
                    {
                        try
                        {
                            await _statusPublisher.PublishIfDueAsync(
                                    _serviceId,
                                    registration.ViewName,
                                    registration.ViewVersion,
                                    snapshot,
                                    MvProjectionStatusPublisherKind.HostedWorker,
                                    stoppingToken)
                                .ConfigureAwait(false);
                        }
                        catch (Exception statusException) when (statusException is not OperationCanceledException)
                        {
                            _logger.LogWarning(
                                statusException,
                                "Unable to publish verify-only startup health for {ViewName}/{ViewVersion}.",
                                registration.ViewName,
                                registration.ViewVersion);
                        }
                    }
                }

                await Task.Delay(GetVerificationRetryDelay(), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private TimeSpan GetVerificationRetryDelay() =>
        _options.VerifyOnlyRetryDelay > TimeSpan.Zero
            ? _options.VerifyOnlyRetryDelay
            : _options.PollInterval > TimeSpan.Zero
                ? _options.PollInterval
                : TimeSpan.FromSeconds(1);

    private async Task<CatchUpCycleResult> RunCatchUpCycleAsync(CancellationToken stoppingToken)
    {
        var appliedEvents = 0;
        var shouldDelay = false;
        var lastSnapshot = MvProjectionStatusSnapshot.Unknown(MvStatus.CatchingUp);

        foreach (var registration in _registrations)
        {
            var projectorResult = await ProcessProjectorAsync(registration, stoppingToken).ConfigureAwait(false);
            lastSnapshot = projectorResult.ProjectionStatus;
            if (_statusPublisher is not null)
            {
                await _statusPublisher.PublishIfDueAsync(
                        _serviceId,
                        registration.ViewName,
                        registration.ViewVersion,
                        projectorResult.ProjectionStatus,
                        MvProjectionStatusPublisherKind.HostedWorker,
                        stoppingToken)
                    .ConfigureAwait(false);
            }

            if (projectorResult.ShouldStop)
            {
                return projectorResult;
            }

            appliedEvents += projectorResult.AppliedEvents;
            shouldDelay |= projectorResult.ShouldDelay;
        }

        return new CatchUpCycleResult(appliedEvents, shouldDelay, ShouldStop: false, lastSnapshot);
    }

    private async Task<CatchUpCycleResult> ProcessProjectorAsync(
        MvApplyHostRegistration registration,
        CancellationToken stoppingToken)
    {
        var host = _hostFactory.Create(registration.ViewName, registration.ViewVersion);
        try
        {
            var result = await _executor.CatchUpOnceAsync(host, _serviceId, stoppingToken).ConfigureAwait(false);
            _failureCounts.Remove(GetProjectorKey(registration));
            var snapshot = result.ProjectionStatus ?? GetSnapshot(registration);
            _publicationSnapshots[GetProjectorKey(registration)] = snapshot;
            return new CatchUpCycleResult(result.AppliedEvents, result.ReachedUnsafeWindow, ShouldStop: false, snapshot);
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(
                ex,
                "Materialized view worker stopped because the configured event store cannot stream all events for {ViewName}/{ViewVersion}.",
                registration.ViewName,
                registration.ViewVersion);
            var snapshot = GetSnapshot(registration) with { Status = MvStatus.Faulted };
            _publicationSnapshots[GetProjectorKey(registration)] = snapshot;
            return new CatchUpCycleResult(0, ShouldDelay: false, ShouldStop: true, snapshot);
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
                var halted = GetSnapshot(registration) with { Status = MvStatus.Faulted };
                _publicationSnapshots[GetProjectorKey(registration)] = halted;
                return new CatchUpCycleResult(0, ShouldDelay: false, ShouldStop: true, halted);
            }

            _logger.LogWarning(
                ex,
                "Materialized view worker retrying {ViewName}/{ViewVersion} after failure {FailureCount}/{MaxFailures}.",
                registration.ViewName,
                registration.ViewVersion,
                failures,
                _options.MaxConsecutiveFailuresBeforeStop);
            var retrying = GetSnapshot(registration) with { Status = MvStatus.Faulted };
            _publicationSnapshots[GetProjectorKey(registration)] = retrying;
            return new CatchUpCycleResult(0, ShouldDelay: true, ShouldStop: false, retrying);
        }
    }

    private MvProjectionStatusSnapshot GetSnapshot(MvApplyHostRegistration registration) =>
        _publicationSnapshots.TryGetValue(GetProjectorKey(registration), out var snapshot)
            ? snapshot
            : MvProjectionStatusSnapshot.Unknown(MvStatus.CatchingUp);

    private int IncrementFailureCount(MvApplyHostRegistration registration)
    {
        var key = GetProjectorKey(registration);
        var failures = _failureCounts.TryGetValue(key, out var currentFailures) ? currentFailures + 1 : 1;
        _failureCounts[key] = failures;
        return failures;
    }

    private static string GetProjectorKey(MvApplyHostRegistration registration) =>
        $"{registration.ViewName}:{registration.ViewVersion}";

    private static string ResolveWorkerServiceId(string? serviceId, MvOptions options)
    {
        var requested = string.IsNullOrWhiteSpace(serviceId) ? options.ServiceId : serviceId;
        if (string.IsNullOrWhiteSpace(requested))
        {
            throw new InvalidOperationException(
                "MvCatchUpWorker requires an exact ServiceId. Configure MvOptions.ServiceId or register a service-bound worker.");
        }

        var normalized = ServiceIdValidator.NormalizeAndValidate(requested);
        if (!string.IsNullOrWhiteSpace(serviceId) && !string.IsNullOrWhiteSpace(options.ServiceId))
        {
            var configured = ServiceIdValidator.NormalizeAndValidate(options.ServiceId);
            if (!string.Equals(configured, normalized, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"MvCatchUpWorker requested ServiceId '{normalized}', but MvOptions is bound to '{configured}'.");
            }
        }

        if (string.Equals(normalized, DefaultServiceIdProvider.DefaultServiceId, StringComparison.Ordinal) &&
            !options.AllowDefaultServiceId)
        {
            throw new InvalidOperationException(
                "MvCatchUpWorker cannot use the implicit default ServiceId. Set AllowDefaultServiceId only for an explicit single-service compatibility registration.");
        }

        return normalized;
    }

    private sealed record CatchUpCycleResult(
        int AppliedEvents,
        bool ShouldDelay,
        bool ShouldStop,
        MvProjectionStatusSnapshot ProjectionStatus);
}
