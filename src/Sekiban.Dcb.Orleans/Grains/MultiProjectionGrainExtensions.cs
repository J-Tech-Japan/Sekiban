using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Hosting;
using ResultBoxes;
using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
/// Extension methods for configuring multi-projection grains
/// </summary>
public static class MultiProjectionGrainExtensions
{
    /// <summary>
    /// Add multi-projection grain support to the silo
    /// </summary>
    public static ISiloBuilder AddMultiProjectionGrain(
        this ISiloBuilder siloBuilder,
        Action<MultiProjectionGrainOptions>? configureOptions = null)
    {
        var options = new MultiProjectionGrainOptions();
        configureOptions?.Invoke(options);
        
        siloBuilder.ConfigureServices(services =>
        {
            services.AddSingleton(options);
        });
        
        // Configure grain storage based on options
        if (options.UseMemoryStorage)
        {
            siloBuilder.AddMemoryGrainStorage("OrleansStorage");
        }
        
        return siloBuilder;
    }
    
    /// <summary>
    /// Add multi-projection grain client support
    /// </summary>
    public static IClientBuilder AddMultiProjectionGrainClient(this IClientBuilder clientBuilder)
    {
        // Client-side configuration if needed
        return clientBuilder;
    }
    
    /// <summary>
    /// Get or create a multi-projection grain
    /// </summary>
    public static IMultiProjectionGrain GetMultiProjectionGrain(
        this IGrainFactory grainFactory,
        string projectorName)
    {
        return grainFactory.GetGrain<IMultiProjectionGrain>(projectorName);
    }
    
    /// <summary>
    /// Get or create a multi-projection grain from cluster client
    /// </summary>
    public static IMultiProjectionGrain GetMultiProjectionGrain(
        this IClusterClient clusterClient,
        string projectorName)
    {
        return clusterClient.GetGrain<IMultiProjectionGrain>(projectorName);
    }
}

/// <summary>
/// Options for multi-projection grain configuration
/// </summary>
public class MultiProjectionGrainOptions
{
    /// <summary>
    /// Maximum state size in bytes (default: 2MB)
    /// </summary>
    public int MaxStateSize { get; set; } = 2 * 1024 * 1024;
    
    /// <summary>
    /// Interval for automatic state persistence (default: 5 minutes)
    /// </summary>
    public TimeSpan PersistInterval { get; set; } = TimeSpan.FromMinutes(5);
    
    /// <summary>
    /// Safe window duration for event ordering (default: 20 seconds)
    /// </summary>
    public TimeSpan SafeWindowDuration { get; set; } = TimeSpan.FromSeconds(20);
    
    /// <summary>
    /// Batch size for event processing (default: 1000)
    /// </summary>
    public int EventBatchSize { get; set; } = 1000;
    
    /// <summary>
    /// Use memory storage for development/testing (default: false)
    /// </summary>
    public bool UseMemoryStorage { get; set; } = false;
    
    /// <summary>
    /// Storage provider name (default: "OrleansStorage")
    /// </summary>
    public string StorageProviderName { get; set; } = "OrleansStorage";
}

/// <summary>
/// Helper service for working with multi-projection grains
/// </summary>
public class MultiProjectionGrainService
{
    private readonly IClusterClient _clusterClient;
    
    public MultiProjectionGrainService(IClusterClient clusterClient)
    {
        _clusterClient = clusterClient ?? throw new ArgumentNullException(nameof(clusterClient));
    }
    
    /// <summary>
    /// Get the state of a multi-projection
    /// </summary>
    public async Task<ResultBox<MultiProjectionState>> GetProjectionStateAsync(
        string projectorName,
        bool canGetUnsafeState = true)
    {
        var grain = _clusterClient.GetMultiProjectionGrain(projectorName);
        return await grain.GetStateAsync(canGetUnsafeState);
    }
    
    /// <summary>
    /// Get the status of a multi-projection grain
    /// </summary>
    public async Task<MultiProjectionGrainStatus> GetProjectionStatusAsync(string projectorName)
    {
        var grain = _clusterClient.GetMultiProjectionGrain(projectorName);
        return await grain.GetStatusAsync();
    }
    
    /// <summary>
    /// Force persist the state of a multi-projection
    /// </summary>
    public async Task<ResultBox<bool>> PersistProjectionStateAsync(string projectorName)
    {
        var grain = _clusterClient.GetMultiProjectionGrain(projectorName);
        return await grain.PersistStateAsync();
    }
    
    /// <summary>
    /// Start or restart subscription for a multi-projection
    /// </summary>
    public async Task StartProjectionSubscriptionAsync(string projectorName)
    {
        var grain = _clusterClient.GetMultiProjectionGrain(projectorName);
        await grain.StartSubscriptionAsync();
    }
    
    /// <summary>
    /// Stop subscription for a multi-projection
    /// </summary>
    public async Task StopProjectionSubscriptionAsync(string projectorName)
    {
        var grain = _clusterClient.GetMultiProjectionGrain(projectorName);
        await grain.StopSubscriptionAsync();
    }
    
    /// <summary>
    /// Get status of all known multi-projections
    /// </summary>
    public async Task<Dictionary<string, MultiProjectionGrainStatus>> GetAllProjectionStatusesAsync(
        IEnumerable<string> projectorNames)
    {
        var tasks = projectorNames.Select(async name =>
        {
            try
            {
                var status = await GetProjectionStatusAsync(name);
                return (name, status);
            }
            catch
            {
                // Return a default status if grain fails
                return (name, new MultiProjectionGrainStatus(
                    name, false, false, null, 0, null, null, 0, true, "Failed to get status"));
            }
        });
        
        var results = await Task.WhenAll(tasks);
        return results.ToDictionary(r => r.name, r => r.Item2);
    }
}

/// <summary>
/// Hosted service for managing multi-projection grains
/// </summary>
public class MultiProjectionGrainHostedService : IHostedService
{
    private readonly IClusterClient _clusterClient;
    private readonly MultiProjectionGrainService _service;
    private readonly List<string> _projectorNames;
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