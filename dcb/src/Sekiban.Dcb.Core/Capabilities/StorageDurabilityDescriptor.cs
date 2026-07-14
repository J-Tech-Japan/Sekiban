namespace Sekiban.Dcb.Capabilities;

/// <summary>
///     Whether a store keeps what it is given.
/// </summary>
public enum StorageDurability
{
    /// <summary>
    ///     The store did not say. Treated as unsafe: a store that cannot state its own durability is not evidence of
    ///     durability, and in Production the guard fails closed on it.
    /// </summary>
    Unknown = 0,

    /// <summary>
    ///     Everything written is lost when the process ends. Correct for unit tests, catastrophic in Production.
    /// </summary>
    Volatile = 1,

    /// <summary>
    ///     Writes survive the process — a database, a managed service, a disk.
    /// </summary>
    Durable = 2
}

/// <summary>
///     What a store says about itself when something asks at runtime.
///     The name of a type is not evidence. A production system once registered an in-memory executor whose one-argument
///     constructor quietly created a private volatile store, and nothing at startup could tell that events were never
///     reaching Cosmos — because nothing could ask. This is the thing that can be asked.
/// </summary>
/// <param name="Durability">Whether writes survive the process.</param>
/// <param name="ProviderName">
///     A human name for the store, for the banner and the failure message: "Postgres", "InMemory",
///     "HybridEventStore(CosmosDb)". Never a connection string, and never anything derived from one.
/// </param>
public sealed record StorageDurabilityDescriptor(StorageDurability Durability, string ProviderName)
{
    /// <summary>For a store that has nothing to say — including one that wraps an inner store that had nothing to say.</summary>
    public static StorageDurabilityDescriptor Unknown(string providerName) =>
        new(StorageDurability.Unknown, providerName);

    /// <summary>e.g. <c>Postgres (Durable)</c>.</summary>
    public override string ToString() => $"{ProviderName} ({Durability})";
}

/// <summary>
///     Implemented by event stores and projection state stores that can state their own durability.
///     A decorator implements this by asking what it wraps — the answer must describe where the data actually lands,
///     not the wrapper. A store that does not implement this is <see cref="StorageDurability.Unknown" />, which is a
///     deliberate default: silence is not a promise of durability.
/// </summary>
public interface IStorageDurabilityDescriptorProvider
{
    /// <summary>Describes this store as it is actually configured, at the moment it is asked.</summary>
    StorageDurabilityDescriptor DescribeStorage();
}
