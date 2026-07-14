using Microsoft.Extensions.DependencyInjection;
namespace Sekiban.Dcb.Capabilities;

/// <summary>
///     The registration an application calls. It is opt-in, and nothing registers it for you.
/// </summary>
public static class SekibanDcbProductionGuardExtensions
{
    /// <summary>
    ///     Refuses to start a Production host whose resolved executor is not a distributed runtime, or whose resolved
    ///     stores do not durably keep what they are given.
    ///     Call it AFTER the registrations it is meant to check — it evaluates what the container actually builds, so
    ///     a later <c>AddSingleton&lt;ISekibanExecutor&gt;</c> that replaces the executor is still caught, but only
    ///     because the check happens at startup rather than here.
    ///     Outside the configured Production environments it validates nothing and only logs the banner.
    /// </summary>
    /// <param name="services">The application's service collection.</param>
    /// <param name="configure">
    ///     Optional. The only override is <c>AllowVolatileStorageInProduction</c>, and it authorises storage only —
    ///     there is no override that permits a testing executor in Production.
    /// </param>
    public static IServiceCollection AddSekibanDcbProductionGuard(
        this IServiceCollection services,
        Action<SekibanDcbProductionGuardOptions>? configure = null) =>
        services.AddSekibanDcbProductionGuardCore(sp => sp.GetService<ISekibanExecutor>(), configure);

    /// <summary>
    ///     Logs the startup banner — environment, executor runtime, store durability, overrides in use, and a loud
    ///     warning when storage is volatile — and never fails the host. What local development wants.
    /// </summary>
    public static IServiceCollection AddSekibanDcbStartupBanner(
        this IServiceCollection services,
        Action<SekibanDcbProductionGuardOptions>? configure = null) =>
        services.AddSekibanDcbStartupBannerCore(sp => sp.GetService<ISekibanExecutor>(), configure);
}
