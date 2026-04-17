using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sekiban.Dcb.MaterializedView.Orleans;

internal sealed class MaterializedViewGrainActivator : BackgroundService
{
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<MaterializedViewGrainActivator> _logger;
    private readonly IReadOnlyList<MvApplyHostRegistration> _registrations;
    private readonly Sekiban.Dcb.ServiceId.IServiceIdProvider _serviceIdProvider;

    public MaterializedViewGrainActivator(
        IMvApplyHostFactory hostFactory,
        IGrainFactory grainFactory,
        Sekiban.Dcb.ServiceId.IServiceIdProvider serviceIdProvider,
        ILogger<MaterializedViewGrainActivator> logger)
    {
        _grainFactory = grainFactory;
        _serviceIdProvider = serviceIdProvider;
        _logger = logger;
        _registrations = hostFactory.GetRegistrations();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var serviceId = _serviceIdProvider.GetCurrentServiceId();
        foreach (var registration in _registrations)
        {
            try
            {
                var grainKey = MvGrainKey.Build(serviceId, registration.ViewName, registration.ViewVersion);
                var grain = _grainFactory.GetGrain<IMaterializedViewGrain>(grainKey);
                await grain.EnsureStartedAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to activate materialized view grain for {ViewName}/{ViewVersion}.",
                    registration.ViewName,
                    registration.ViewVersion);
            }
        }
    }
}
