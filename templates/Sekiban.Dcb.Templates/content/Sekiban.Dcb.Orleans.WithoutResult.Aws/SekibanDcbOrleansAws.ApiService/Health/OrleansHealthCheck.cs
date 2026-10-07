using Microsoft.Extensions.Diagnostics.HealthChecks;
using Orleans.Runtime;

namespace SekibanDcbOrleansAws.ApiService.Health;

/// <summary>
///     Readiness: checks whether the local Orleans silo is Active.
/// </summary>
public class OrleansHealthCheck(ISiloStatusOracle siloStatusOracle) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var status = siloStatusOracle.CurrentStatus;
        return Task.FromResult(status == SiloStatus.Active
            ? HealthCheckResult.Healthy("The local Orleans silo is Active")
            : HealthCheckResult.Unhealthy($"The local Orleans silo is {status}"));
    }
}
