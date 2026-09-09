using Microsoft.Extensions.DependencyInjection;

namespace Sekiban.Dcb.SizeGates;

public static class SekibanDcbExecutorSizeGateExtensions
{
    /// <summary>
    /// Registers one opt-in executor size policy set. No service is added to the container when this method is not
    /// called, so existing construction remains ungated.
    /// </summary>
    public static IServiceCollection AddSekibanDcbExecutorSizeGate(
        this IServiceCollection services,
        Action<ExecutorSizeGateOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ExecutorSizeGateOptions();
        configure(options);
        options.Validate();
        services.AddSingleton(options);
        return services;
    }

    public static IServiceCollection AddSekibanDcbExecutorSizeGate(
        this IServiceCollection services,
        ExecutorSizeGateOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        services.AddSingleton(options);
        return services;
    }
}
