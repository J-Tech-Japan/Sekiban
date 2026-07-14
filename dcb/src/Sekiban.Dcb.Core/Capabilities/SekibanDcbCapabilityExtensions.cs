using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace Sekiban.Dcb.Capabilities;

/// <summary>
///     Registration for the startup banner and the production guard.
///     Both are opt-in and neither changes how anything executes. The guard is never auto-registered by a provider or
///     a template: a library that decides on its own when your host may not start is a library that will one day be
///     wrong about it.
/// </summary>
public static class SekibanDcbCapabilityExtensions
{
    /// <summary>
    ///     Registers the guard. The package that knows what <c>ISekibanExecutor</c> is passes in the lookup — see
    ///     <c>AddSekibanDcbProductionGuard</c> in Sekiban.Dcb.WithResult / Sekiban.Dcb.WithoutResult, which is what
    ///     applications call.
    /// </summary>
    public static IServiceCollection AddSekibanDcbProductionGuardCore(
        this IServiceCollection services,
        Func<IServiceProvider, object?> resolveExecutor,
        Action<SekibanDcbProductionGuardOptions>? configure = null) =>
        services.AddSekibanDcbStartupValidator(resolveExecutor, configure, true);

    /// <summary>
    ///     Registers the banner alone: it reports what was resolved and warns loudly about volatile storage, and it
    ///     never fails the host. For local development, where a volatile store is the point.
    /// </summary>
    public static IServiceCollection AddSekibanDcbStartupBannerCore(
        this IServiceCollection services,
        Func<IServiceProvider, object?> resolveExecutor,
        Action<SekibanDcbProductionGuardOptions>? configure = null) =>
        services.AddSekibanDcbStartupValidator(resolveExecutor, configure, false);

    private static IServiceCollection AddSekibanDcbStartupValidator(
        this IServiceCollection services,
        Func<IServiceProvider, object?> resolveExecutor,
        Action<SekibanDcbProductionGuardOptions>? configure,
        bool enforce)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(resolveExecutor);

        var options = new SekibanDcbProductionGuardOptions();
        configure?.Invoke(options);

        services.AddSingleton<IHostedService>(sp => new SekibanDcbStartupValidator(
            sp,
            sp.GetRequiredService<IHostEnvironment>(),
            options,
            resolveExecutor,
            enforce,
            sp.GetRequiredService<ILogger<SekibanDcbStartupValidator>>()));

        return services;
    }
}
