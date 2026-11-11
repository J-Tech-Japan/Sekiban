using Microsoft.Extensions.Caching.Memory;
namespace Sekiban.Infrastructure.Cosmos;

// One store per DI container. Only clients constructed by Sekiban are owned here.
internal sealed class CosmosClientStore : IDisposable
{
    private readonly object sync = new();
    private readonly Dictionary<CosmosClientKey, CosmosClient> clients = new();
    private bool disposed;

    public CosmosClientStore() { }

    // Handles must not outlive their clients or cross independent DI containers.
    public IMemoryCache Cache { get; } = new MemoryCache(new MemoryCacheOptions());

    public CosmosClient GetOrCreate(CosmosClientKey key, Func<CosmosClient> create)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (clients.TryGetValue(key, out var client)) { return client; }
            // Construction is synchronous. A failed constructor leaves no poisoned entry.
            client = create();
            clients.Add(key, client);
            return client;
        }
    }

    public void ThrowIfDisposed()
    {
        lock (sync) { ObjectDisposedException.ThrowIf(disposed, this); }
    }

    public void Dispose()
    {
        CosmosClient[] ownedClients;
        lock (sync)
        {
            if (disposed) { return; }
            disposed = true;
            ownedClients = clients.Values.Distinct<CosmosClient>(ReferenceEqualityComparer.Instance).ToArray();
            clients.Clear();
            Cache.Dispose();
        }
        List<Exception>? errors = null;
        foreach (var client in ownedClients)
        {
            try { client.Dispose(); }
            catch (Exception error) { (errors ??= new()).Add(error); }
        }
        if (errors is not null) { throw new AggregateException(errors); }
    }
}

internal readonly record struct CosmosClientKey(
    string Context,
    string? ConnectionString,
    string? Endpoint,
    string? AuthorizationKey,
    CosmosClientOptions Options)
{
    // Do not expose credentials if a cache key is formatted for diagnostics.
    public override string ToString() => $"Cosmos client ({Context})";
}
