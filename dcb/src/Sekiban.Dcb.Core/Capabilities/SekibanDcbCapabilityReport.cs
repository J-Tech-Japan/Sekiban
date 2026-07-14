namespace Sekiban.Dcb.Capabilities;

/// <summary>
///     What was actually resolved out of the container, and what it said about itself.
///     Assembled from live instances, not from registrations: a decorator, a factory or a lambda that quietly swaps the
///     real store for something else is visible here, because here is where we ask the thing that got built.
/// </summary>
/// <param name="EnvironmentName">The host environment, e.g. "Production".</param>
/// <param name="IsProductionEnvironment">Whether the guard considers that environment a production one.</param>
/// <param name="ExecutorTypeName">The concrete executor type that was resolved, or null if none was registered.</param>
/// <param name="Executor">What the executor said about its runtime.</param>
/// <param name="EventStore">What the event store said about its durability.</param>
/// <param name="ProjectionStore">What the projection state store said about its durability.</param>
/// <param name="UsedOverrideNames">Overrides the operator turned on, by name.</param>
public sealed record SekibanDcbCapabilityReport(
    string EnvironmentName,
    bool IsProductionEnvironment,
    string? ExecutorTypeName,
    ExecutorRuntimeDescriptor Executor,
    StorageDurabilityDescriptor EventStore,
    StorageDurabilityDescriptor ProjectionStore,
    IReadOnlyList<string> UsedOverrideNames)
{
    /// <summary>True when any resolved store does not durably keep what it is given.</summary>
    public bool HasVolatileOrUnknownStorage =>
        EventStore.Durability != StorageDurability.Durable || ProjectionStore.Durability != StorageDurability.Durable;

    /// <summary>True when the resolved executor is not a real distributed runtime.</summary>
    public bool HasTestingOrUnknownExecutor => Executor.Runtime != ExecutorRuntimeKind.DistributedRuntime;
}
