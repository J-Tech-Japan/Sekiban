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

    /// <summary>
    ///     What write-conditions a store can enforce, or nothing (<see cref="WriteConditionCapabilityDescriptor.None" />)
    ///     if it says nothing. Resolved from the live instance — a store that does not implement
    ///     <see cref="IWriteConditionCapabilityProvider" /> supports nothing, never "maybe". This is what the conditional
    ///     append path consults to fail closed before it does any work.
    /// </summary>
    public static WriteConditionCapabilityDescriptor DescribeWriteConditions(object? store, string role) =>
        store switch
        {
            null => WriteConditionCapabilityDescriptor.None($"(no {role} registered)"),
            IWriteConditionCapabilityProvider provider => provider.DescribeWriteConditions(),
            _ => WriteConditionCapabilityDescriptor.None(store.GetType().Name)
        };

    /// <summary>
    ///     What tagged callback-streaming a store explicitly declares. A missing declaration is no capability, even if a
    ///     type happens to expose the optional stream interface.
    /// </summary>
    public static TaggedStreamCapabilityDescriptor DescribeTaggedStream(object? store, string role) =>
        store switch
        {
            null => TaggedStreamCapabilityDescriptor.None($"(no {role} registered)"),
            ITaggedStreamCapabilityProvider provider => provider.DescribeTaggedStream(),
            _ => TaggedStreamCapabilityDescriptor.None(store.GetType().Name)
        };

    /// <summary>
    ///     Resolves tagged streaming from the live instance. Both the optional interface and an honest native declaration
    ///     are required, so silent and deceptive providers remain on the safe list fallback.
    /// </summary>
    public static TaggedStreamCapabilityResolution ResolveTaggedStream(object? store, string role)
    {
        var descriptor = DescribeTaggedStream(store, role);
        if (store is not IStreamingTaggedSerializableEventStore streamStore)
        {
            return new TaggedStreamCapabilityResolution(
                false,
                null,
                descriptor,
                $"{descriptor.ProviderName} does not implement {nameof(IStreamingTaggedSerializableEventStore)}.");
        }

        if (!descriptor.NativeStreaming)
        {
            return new TaggedStreamCapabilityResolution(
                false,
                null,
                descriptor,
                $"{descriptor.ProviderName} does not declare native tagged streaming.");
        }

        return new TaggedStreamCapabilityResolution(true, streamStore, descriptor, null);
    }

    /// <summary>
    ///     Applies the tagged-stream order policy at consumer boundaries. Equal ids preserve the existing duplicate
    ///     policy; a smaller id is a contract violation and callers must fail before publishing their partial state.
    /// </summary>
    public static bool IsTaggedStreamOrderValid(string? previousId, string currentId, out bool isDuplicate)
    {
        isDuplicate = false;
        if (string.IsNullOrEmpty(previousId))
        {
            return true;
        }

        var comparison = string.Compare(currentId, previousId, StringComparison.Ordinal);
        if (comparison < 0)
        {
            return false;
        }

        isDuplicate = comparison == 0;
        return true;
    }
}
