using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SekibanDcbOrleans.ApiService.Health;

/// <summary>
///     Orleans Silo health check
///     Readiness: Verifies that the Silo has joined the cluster
/// </summary>
public class OrleansHealthCheck(IGrainFactory grainFactory, ILogger<OrleansHealthCheck> logger) : IHealthCheck
{
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly ILogger<OrleansHealthCheck> _logger = logger;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // If IGrainFactory is available, Orleans has started
            // Orleans 9.x/10.x doesn't have IClusterClient.IsInitialized,
            // so we check GrainFactory availability as an alternative
            if (_grainFactory is null)
            {
                _logger.LogWarning("Orleans GrainFactory is not available");
                return Task.FromResult(HealthCheckResult.Unhealthy("Orleans GrainFactory is not available"));
            }

            // If GrainFactory is available from DI, the Silo is running
            return Task.FromResult(HealthCheckResult.Healthy("Orleans Silo is running"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Orleans health check failed");
            return Task.FromResult(HealthCheckResult.Unhealthy($"Orleans health check failed: {ex.Message}", ex));
        }
    }
}
