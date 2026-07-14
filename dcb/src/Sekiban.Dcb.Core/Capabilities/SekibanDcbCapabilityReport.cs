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
    /// <summary>
    ///     True when a store told us, in as many words, that it does not keep what it is given.
    ///     This is the only thing <c>AllowVolatileStorageInProduction</c> can authorise: an operator can look at a
    ///     store that says "Volatile" and decide they meant it.
    /// </summary>
    public bool HasVolatileStorage =>
        EventStore.Durability == StorageDurability.Volatile
        || ProjectionStore.Durability == StorageDurability.Volatile;

    /// <summary>
    ///     True when a store would not say what it is.
    ///     Deliberately NOT the same condition as <see cref="HasVolatileStorage" />, and deliberately not overridable.
    ///     An operator who ticks "I accept volatile storage" has accepted a store that declared itself volatile — they
    ///     have not accepted an unidentified store that might be anything, because there is nothing there to accept.
    /// </summary>
    public bool HasUnknownStorage =>
        EventStore.Durability == StorageDurability.Unknown
        || ProjectionStore.Durability == StorageDurability.Unknown;

    /// <summary>True when any resolved store does not durably keep what it is given, for the banner's warning.</summary>
    public bool HasVolatileOrUnknownStorage => HasVolatileStorage || HasUnknownStorage;

    /// <summary>True when the resolved executor is not a real distributed runtime.</summary>
    public bool HasTestingOrUnknownExecutor => Executor.Runtime != ExecutorRuntimeKind.DistributedRuntime;
}
