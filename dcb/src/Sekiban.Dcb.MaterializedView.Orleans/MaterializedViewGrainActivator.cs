using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sekiban.Dcb.MaterializedView.Orleans;

internal sealed class MaterializedViewGrainActivator : BackgroundService
{
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<MaterializedViewGrainActivator> _logger;
    private readonly IReadOnlyList<IMaterializedViewProjector> _projectors;
    private readonly Sekiban.Dcb.ServiceId.IServiceIdProvider _serviceIdProvider;

    public MaterializedViewGrainActivator(
        IEnumerable<IMaterializedViewProjector> projectors,
        IGrainFactory grainFactory,
        Sekiban.Dcb.ServiceId.IServiceIdProvider serviceIdProvider,
        ILogger<MaterializedViewGrainActivator> logger)
    {
        _grainFactory = grainFactory;
        _serviceIdProvider = serviceIdProvider;
        _logger = logger;
        _projectors = projectors.ToList();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var serviceId = _serviceIdProvider.GetCurrentServiceId();
        foreach (var projector in _projectors)
        {
            try
            {
                var grainKey = MvGrainKey.Build(serviceId, projector.ViewName, projector.ViewVersion);
                var grain = _grainFactory.GetGrain<IMaterializedViewGrain>(grainKey);
                await grain.EnsureStartedAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to activate materialized view grain for {ViewName}/{ViewVersion}.",
                    projector.ViewName,
                    projector.ViewVersion);
            }
        }
    }
}
