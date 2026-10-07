using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SekibanDcbDecider.ApiService.Health;

/// <summary>
///     Readiness: checks whether authentication database initialization has completed.
/// </summary>
public class AuthInitializationHealthCheck : IHealthCheck
{
    private volatile bool _isInitialized;

    public void MarkInitialized() => _isInitialized = true;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) => Task.FromResult(_isInitialized
            ? HealthCheckResult.Healthy("Authentication database initialization completed")
            : HealthCheckResult.Unhealthy("Authentication database initialization has not completed"));
}
