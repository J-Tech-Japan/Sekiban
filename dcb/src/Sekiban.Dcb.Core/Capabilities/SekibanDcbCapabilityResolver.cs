using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sekiban.Dcb.Storage;
namespace Sekiban.Dcb.Capabilities;

/// <summary>
///     Asks the container what it actually built, and asks each thing it built what it is.
///     Everything here resolves live instances. Reading the <c>IServiceCollection</c> instead would be easier and
///     wrong: the registration says <c>AddSekibanDcbCosmosDb</c>, and the instance is what a decorator, a factory or a
///     later <c>AddSingleton</c> override actually handed back.
/// </summary>
public static class SekibanDcbCapabilityResolver
{
    /// <summary>
    ///     Resolves the executor and both stores, and asks each what it is.
    /// </summary>
    /// <param name="services">The built container.</param>
    /// <param name="environment">The host environment, for the Production decision.</param>
    /// <param name="options">Which environments count as Production, and which overrides are on.</param>
    /// <param name="resolveExecutor">
    ///     How to get the executor. <c>ISekibanExecutor</c> is declared in the WithResult and WithoutResult packages,
    ///     which sit above this one, so the package that knows the interface passes in the lookup.
    /// </param>
    public static SekibanDcbCapabilityReport Resolve(
        IServiceProvider services,
        IHostEnvironment environment,
        SekibanDcbProductionGuardOptions options,
        Func<IServiceProvider, object?> resolveExecutor)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(resolveExecutor);

        var executor = resolveExecutor(services);

        var eventStore = (object?)services.GetService<IEventStore>() ?? services.GetService<IEventStoreFactory>();
        var projectionStore = (object?)services.GetService<IMultiProjectionStateStore>()
            ?? services.GetService<IMultiProjectionStateStoreFactory>();

        var isProduction = options
            .ProductionEnvironmentNames
            .Any(name => string.Equals(name, environment.EnvironmentName, StringComparison.OrdinalIgnoreCase));

        return new SekibanDcbCapabilityReport(
            environment.EnvironmentName,
            isProduction,
            executor?.GetType().FullName,
            DescribeExecutor(executor),
            DescribeStorage(eventStore, "event store"),
            DescribeStorage(projectionStore, "projection state store"),
            options.UsedOverrideNames());
    }

    /// <summary>
    ///     What the executor says, or <see cref="ExecutorRuntimeKind.Unknown" /> if it says nothing. Saying nothing is
    ///     not the same as saying "distributed", and the guard must never read it that way.
    /// </summary>
    public static ExecutorRuntimeDescriptor DescribeExecutor(object? executor) =>
        executor switch
        {
            null => ExecutorRuntimeDescriptor.Unknown("(no ISekibanExecutor registered)"),
            IExecutorRuntimeDescriptorProvider provider => provider.DescribeRuntime(),
            _ => ExecutorRuntimeDescriptor.Unknown(executor.GetType().Name)
        };

    /// <summary>What a store says, or <see cref="StorageDurability.Unknown" /> if it says nothing.</summary>
    public static StorageDurabilityDescriptor DescribeStorage(object? store, string role) =>
        store switch
        {
            null => StorageDurabilityDescriptor.Unknown($"(no {role} registered)"),
            IStorageDurabilityDescriptorProvider provider => provider.DescribeStorage(),
            _ => StorageDurabilityDescriptor.Unknown(store.GetType().Name)
        };
}
