using Microsoft.Extensions.Hosting;
namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Hosted service for managing multi-projection grains
/// </summary>
public class MultiProjectionGrainHostedService : IHostedService
{
    private readonly IClusterClient _clusterClient;
    private readonly List<string> _projectorNames;
    private readonly MultiProjectionGrainService _service;
    private Timer? _statusTimer;

    public MultiProjectionGrainHostedService(
        IClusterClient clusterClient,
        MultiProjectionGrainService service,
        IEnumerable<string> projectorNames)
    {
        _clusterClient = clusterClient;
        _service = service;
        _projectorNames = projectorNames.ToList();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Start subscriptions for all configured projectors
        foreach (var projectorName in _projectorNames)
        {
            try
            {
                await _service.StartProjectionSubscriptionAsync(projectorName);
            }
            catch (Exception ex)
            {
                // Log error but continue with other projectors
                Console.WriteLine($"Failed to start projection {projectorName}: {ex.Message}");
            }
        }

        // Set up periodic status monitoring
        _statusTimer = new Timer(
            async _ => await MonitorProjectionsAsync(),
            null,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1));
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _statusTimer?.Dispose();

        // Stop all subscriptions gracefully
        foreach (var projectorName in _projectorNames)
        {
            try
            {
                await _service.StopProjectionSubscriptionAsync(projectorName);
            }
            catch
            {
                // Ignore errors during shutdown
            }
        }
    }

    private async Task MonitorProjectionsAsync()
    {
        try
        {
            var statuses = await _service.GetAllProjectionStatusesAsync(_projectorNames);

            foreach (var (name, status) in statuses)
            {
                if (status.HasError)
                {
                    Console.WriteLine($"Projection {name} has error: {status.LastError}");

                    // Try to restart if subscription is not active
                    if (!status.IsSubscriptionActive)
                    {
                        try
                        {
                            await _service.StartProjectionSubscriptionAsync(name);
                            Console.WriteLine($"Restarted projection {name}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to restart projection {name}: {ex.Message}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error monitoring projections: {ex.Message}");
        }
    }
}
