using Microsoft.Extensions.DependencyInjection;

namespace Sekiban.Dcb.SizeGates;

public static class SekibanDcbExecutorSizeGateExtensions
{
    private const string CoreConvenienceMechanism = ExecutorSizeGateRegistrationMarker.CoreConvenience;

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

        ExecutorSizeGateRegistrationMarker.EnsureCompatible(services, CoreConvenienceMechanism);
        var options = new ExecutorSizeGateOptions();
        configure(options);
        options.Validate();
        ExecutorSizeGateRegistrationMarker.Declare(services, CoreConvenienceMechanism);
        services.AddSingleton(options);
        return services;
    }

    public static IServiceCollection AddSekibanDcbExecutorSizeGate(
        this IServiceCollection services,
        ExecutorSizeGateOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ExecutorSizeGateRegistrationMarker.EnsureCompatible(services, CoreConvenienceMechanism);
        options.Validate();
        ExecutorSizeGateRegistrationMarker.Declare(services, CoreConvenienceMechanism);
        services.AddSingleton(options);
        return services;
    }
}

/// <summary>
/// Internal registration identity shared by the Core convenience and provider-composable gate helpers.
/// The marker is held in the caller's service collection so separate registrations cannot silently replace one another.
/// </summary>
internal sealed class ExecutorSizeGateRegistrationMarker
{
    internal const string CoreConvenience = "Core convenience gate registration";
    internal const string ProviderComposable = "provider-composable gate registration";

    private ExecutorSizeGateRegistrationMarker(string mechanism)
    {
        Mechanism = mechanism;
    }

    internal string Mechanism { get; }

    internal static void EnsureCompatible(IServiceCollection services, string mechanism)
    {
        var existing = Find(services);
        if (existing is null || existing.Mechanism == mechanism)
            return;

        throw new InvalidOperationException(
            $"'{existing.Mechanism}' and '{mechanism}' cannot both configure the executor size gate. " +
            "Configure every policy through one mechanism: either a single " +
            "AddSekibanDcbExecutorSizeGate callback that calls the provider policy extensions, " +
            "or only the AddSekibanDcbDynamoDb*SizeGate helpers.");
    }

    internal static void Declare(IServiceCollection services, string mechanism)
    {
        EnsureCompatible(services, mechanism);
        if (Find(services) is null)
            services.AddSingleton(new ExecutorSizeGateRegistrationMarker(mechanism));
    }

    private static ExecutorSizeGateRegistrationMarker? Find(IServiceCollection services) =>
        services.FirstOrDefault(descriptor => descriptor.ServiceType == typeof(ExecutorSizeGateRegistrationMarker))
            ?.ImplementationInstance as ExecutorSizeGateRegistrationMarker;
}
