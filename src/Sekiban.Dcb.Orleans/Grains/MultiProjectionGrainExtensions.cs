using Microsoft.Extensions.DependencyInjection;
namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Extension methods for configuring multi-projection grains
/// </summary>
public static class MultiProjectionGrainExtensions
{
    /// <summary>
    ///     Add multi-projection grain support to the silo
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
    ///     Add multi-projection grain client support
    /// </summary>
    public static IClientBuilder AddMultiProjectionGrainClient(this IClientBuilder clientBuilder) =>
        // Client-side configuration if needed
        clientBuilder;

    /// <summary>
    ///     Get or create a multi-projection grain
    /// </summary>
    public static IMultiProjectionGrain GetMultiProjectionGrain(
        this IGrainFactory grainFactory,
        string projectorName) =>
        grainFactory.GetGrain<IMultiProjectionGrain>(projectorName);

    /// <summary>
    ///     Get or create a multi-projection grain from cluster client
    /// </summary>
    public static IMultiProjectionGrain GetMultiProjectionGrain(
        this IClusterClient clusterClient,
        string projectorName) =>
        clusterClient.GetGrain<IMultiProjectionGrain>(projectorName);
}
