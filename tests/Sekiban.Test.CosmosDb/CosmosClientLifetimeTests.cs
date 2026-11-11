using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Aspire.Infrastructure.Cosmos;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Cache;
using Sekiban.Core.Documents;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Setting;
using Sekiban.Infrastructure.Cosmos;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Sekiban.Test.CosmosDb;

// These tests use real SDK handles or tracked clients, never a live Cosmos account.
public class CosmosClientLifetimeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task ConcurrentFactoriesAndDocumentTypes_ShareOneOwnedClient()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var root = Services(cache).BuildServiceProvider(validateScopes: true);
        using var scope = root.CreateScope();
        const int count = 48;
        var factories = Enumerable.Range(0, count).Select(_ => Factory(scope.ServiceProvider)).ToArray();
        Assert.Equal(count, factories.Distinct().Count());
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;
        foreach (var factory in factories)
            factory.SearchCosmosClientAsync = async () =>
            {
                if (Interlocked.Increment(ref entered) == count) gate.TrySetResult();
                await gate.Task;
                return null;
            };
        var clients = await Task.WhenAll(factories.Select((factory, index) =>
            factory.GetCosmosClientAsync(index % 2 == 0 ? DocumentType.Event : DocumentType.Command))).WaitAsync(Timeout);
        Assert.Single(clients.Distinct());
    }

    [Theory]
    [InlineData("order", false)]
    [InlineData("order", true)]
    [InlineData("duplicate", false)]
    [InlineData("duplicate", true)]
    [InlineData("cosmos", false)]
    [InlineData("cosmos", true)]
    [InlineData("network", false)]
    [InlineData("network", true)]
    [InlineData("cancellation", false)]
    [InlineData("cancellation", true)]
    public async Task ActionFailure_PreservesClientAndHandlesAndPropagatesException(string kind, bool generic)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var root = Services(cache).BuildServiceProvider();
        var factory = Factory(root);
        var client = await factory.GetCosmosClientAsync(DocumentType.Event);
        var container = client.GetContainer(factory.GetDatabaseId(), factory.GetContainerId(DocumentType.Event, AggregateContainerGroup.Default));
        factory.SetContainerToCache(DocumentType.Event, AggregateContainerGroup.Default, container);
        Exception failure = kind switch
        {
            "order" => new SekibanEventOrderMixedUpException(),
            "duplicate" => new SekibanEventDuplicateException(),
            "cosmos" => new CosmosException("Unavailable", HttpStatusCode.ServiceUnavailable, 0, "test", 0),
            "network" => new HttpRequestException("Unavailable"),
            _ => new OperationCanceledException()
        };
        for (var iteration = 0; iteration < 20; iteration++)
        {
            var actual = await Record.ExceptionAsync(async () =>
            {
                if (generic)
                    await factory.CosmosActionAsync<int>(DocumentType.Event, AggregateContainerGroup.Default, _ => Task.FromException<int>(failure));
                else
                    await factory.CosmosActionAsync(DocumentType.Event, AggregateContainerGroup.Default, _ => Task.FromException(failure));
            });
            Assert.Same(failure, actual);
            Assert.Same(client, await factory.GetCosmosClientAsync(DocumentType.Event));
            Assert.Same(container, factory.GetContainerFromCache(DocumentType.Event, AggregateContainerGroup.Default));
        }
        await factory.CosmosActionAsync(DocumentType.Event, AggregateContainerGroup.Default, _ => Task.CompletedTask);
    }

    [Fact]
    public async Task ExplicitReset_InvalidatesHandlesButRetainsClient()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var root = Services(cache).BuildServiceProvider();
        var factory = Factory(root);
        var client = await factory.GetCosmosClientAsync(DocumentType.Event);
        factory.SetContainerToCache(DocumentType.Event, AggregateContainerGroup.Default,
            client.GetContainer(factory.GetDatabaseId(), factory.GetContainerId(DocumentType.Event, AggregateContainerGroup.Default)));
        factory.ResetMemoryCache(DocumentType.Event, AggregateContainerGroup.Default);
        Assert.Null(factory.GetContainerFromCache(DocumentType.Event, AggregateContainerGroup.Default));
        Assert.Same(client, await factory.GetCosmosClientAsync(DocumentType.Event));
    }

    [Fact]
    public async Task ScopeDisposal_RetainsOwnedClientUntilRootDisposal()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var root = Services(cache).BuildServiceProvider(validateScopes: true);
        CosmosClient client;
        using (var scope = root.CreateScope()) client = await Factory(scope.ServiceProvider).GetCosmosClientAsync(DocumentType.Event);
        using (var scope = root.CreateScope()) Assert.Same(client, await Factory(scope.ServiceProvider).GetCosmosClientAsync(DocumentType.Command));
        root.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetDatabase("unused").ReadAsync());
    }

    [Fact]
    public async Task AspireClient_IsDisposedOnlyByItsExternalOwner()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var services = Services(cache);
        services.AddSingleton(new SekibanCosmosAspireOptions("cosmos"));
        services.AddKeyedSingleton<CosmosClient>("cosmos", (_, _) => new TrackingClient());
        services.AddTransient<AspireCosmosDbFactory>();
        using var root = services.BuildServiceProvider(validateScopes: true);
        var external = (TrackingClient)root.GetRequiredKeyedService<CosmosClient>("cosmos");
        for (var iteration = 0; iteration < 2; iteration++)
        {
            using var scope = root.CreateScope();
            var adapter = scope.ServiceProvider.GetRequiredService<AspireCosmosDbFactory>();
            adapter.cosmosDbFactory.SetContainerToCache(DocumentType.Event, AggregateContainerGroup.Default,
                external.GetContainer("SekibanDb", "events"));
            await adapter.CosmosActionAsync(DocumentType.Event, AggregateContainerGroup.Default, _ => Task.CompletedTask);
            Assert.Same(external, await adapter.cosmosDbFactory.GetCosmosClientAsync(DocumentType.Command));
        }
        Assert.Equal(0, external.DisposeCount);
        root.Dispose();
        Assert.Equal(1, external.DisposeCount);
    }

    [Fact]
    public async Task ScopedContexts_RemainIsolated()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var root = Services(cache).BuildServiceProvider(validateScopes: true);
        using var first = root.CreateScope();
        using var second = root.CreateScope();
        var one = Factory(first.ServiceProvider);
        var two = Factory(second.ServiceProvider);
        var client = await one.GetCosmosClientAsync(DocumentType.Event);
        await second.ServiceProvider.GetRequiredService<ISekibanContext>().SekibanActionAsync("Secondary", async () =>
        {
            Assert.NotSame(client, await two.GetCosmosClientAsync(DocumentType.Event));
            one.SetContainerToCache(DocumentType.Event, AggregateContainerGroup.Default, client.GetContainer("SekibanDb", "events"));
            Assert.Null(two.GetContainerFromCache(DocumentType.Event, AggregateContainerGroup.Default));
        });
        Assert.Same(client, await two.GetCosmosClientAsync(DocumentType.Event));
    }

    [Fact]
    public async Task IndependentProviders_DoNotShareClientsOrStaleHandlesThroughMemoryCache()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var services = Services(cache);
        using var first = services.BuildServiceProvider();
        using var second = services.BuildServiceProvider();
        var one = Factory(first);
        var two = Factory(second);
        var client = await one.GetCosmosClientAsync(DocumentType.Event);
        one.SetContainerToCache(DocumentType.Event, AggregateContainerGroup.Default, client.GetContainer("SekibanDb", "events"));
        first.Dispose();
        Assert.NotSame(client, await two.GetCosmosClientAsync(DocumentType.Event));
        Assert.Null(two.GetContainerFromCache(DocumentType.Event, AggregateContainerGroup.Default));
    }

    [Fact]
    public void FailedConstruction_IsRetriedAndOwnedClientIsDisposedExactlyOnce()
    {
        using var store = new CosmosClientStore();
        var key = new CosmosClientKey("Default", null, "test", null, ClientOptions());
        var calls = 0;
        TrackingClient? client = null;
        CosmosClient Create()
        {
            if (++calls == 1) throw new InvalidOperationException("Initialization failed");
            return client = new TrackingClient();
        }
        Assert.Throws<InvalidOperationException>(() => store.GetOrCreate(key, Create));
        Assert.Same(store.GetOrCreate(key, Create), store.GetOrCreate(key, Create));
        Assert.Equal(2, calls);
        store.Dispose();
        store.Dispose();
        Assert.Equal(1, client!.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => store.GetOrCreate(key, Create));
    }

    [Fact]
    public async Task DirectConstructor_RemainsUsableAndOwnsItsFallbackClientStore()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var factory = new CosmosDbFactory(new CacheAccessor(cache), provider, new SekibanCosmosClientOptions { ClientOptions = ClientOptions() }, Settings());
        var client = await factory.GetCosmosClientAsync(DocumentType.Event);
        factory.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetDatabase("unused").ReadAsync());
    }

    [Fact]
    public async Task FailedExternalLookup_DoesNotPoisonSubsequentLookupOrTransferOwnership()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var root = Services(cache).BuildServiceProvider();
        using var external = new TrackingClient();
        var factory = Factory(root);
        factory.SearchCosmosClientAsync = () => Task.FromException<CosmosClient?>(new InvalidOperationException("Lookup failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => factory.GetCosmosClientAsync(DocumentType.Event));
        factory.SearchCosmosClientAsync = () => Task.FromResult<CosmosClient?>(external);
        Assert.Same(external, await factory.GetCosmosClientAsync(DocumentType.Event));
        root.Dispose();
        Assert.Equal(0, external.DisposeCount);
    }

    [Fact]
    public async Task ChangedExternalClient_DoesNotReusePreviousClientsContainer()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var root = Services(cache).BuildServiceProvider();
        using var first = new TrackingClient();
        using var second = new TrackingClient { FailDatabaseCreation = true };
        var factory = Factory(root);
        var container = first.GetContainer("SekibanDb", "events");
        factory.SetContainerToCache(DocumentType.Event, AggregateContainerGroup.Default, container);
        factory.SearchCosmosClientAsync = () => Task.FromResult<CosmosClient?>(first);
        Assert.Same(container, await factory.GetContainerAsync(DocumentType.Event, AggregateContainerGroup.Default));
        factory.SearchCosmosClientAsync = () => Task.FromResult<CosmosClient?>(second);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.GetContainerAsync(DocumentType.Event, AggregateContainerGroup.Default));
        Assert.Equal("New client requires its own database handle", error.Message);
    }

    [Fact]
    public async Task ConcurrentShutdown_DoesNotLeakClientBeingConstructed()
    {
        using var store = new CosmosClientStore();
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var key = new CosmosClientKey("Default", null, "test", null, ClientOptions());
        var client = new TrackingClient();
        var creation = Task.Run(() => store.GetOrCreate(key, () =>
        {
            started.TrySetResult();
            if (!release.Wait(Timeout)) throw new TimeoutException();
            return client;
        }));
        await started.Task.WaitAsync(Timeout);
        var shutdown = Task.Run(() => { disposing.TrySetResult(); store.Dispose(); });
        await disposing.Task.WaitAsync(Timeout);
        release.Set();
        await Task.WhenAll(creation, shutdown).WaitAsync(Timeout);
        Assert.Equal(1, client.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => store.GetOrCreate(key, () => client));
    }

    [Fact]
    public async Task DifferentConnectionsWithinSameContext_DoNotShareClientsOrHandles()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var root = Services(cache).BuildServiceProvider();
        var first = Factory(root);
        using var second = new CosmosDbFactory(new CacheAccessor(cache), root,
            root.GetRequiredService<SekibanCosmosClientOptions>(), new SekibanCosmosDbOptions
            {
                Contexts = [Setting(SekibanContext.Default) with { CosmosEndPointUrl = "https://localhost:8082" }]
            });
        var client = await first.GetCosmosClientAsync(DocumentType.Event);
        first.SetContainerToCache(DocumentType.Event, AggregateContainerGroup.Default, client.GetContainer("SekibanDb", "events"));
        Assert.NotSame(client, await second.GetCosmosClientAsync(DocumentType.Event));
        Assert.Null(second.GetContainerFromCache(DocumentType.Event, AggregateContainerGroup.Default));
    }

    [Fact]
    public async Task ShutdownDuringExternalLookup_DoesNotTakeOwnershipOfLateClient()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var root = Services(cache).BuildServiceProvider();
        using var external = new TrackingClient();
        var pending = new TaskCompletionSource<CosmosClient?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = Factory(root);
        factory.SearchCosmosClientAsync = () => pending.Task;
        var lookup = factory.GetCosmosClientAsync(DocumentType.Event);
        root.Dispose();
        pending.SetResult(external);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => lookup);
        Assert.Equal(0, external.DisposeCount);
    }

    private static CosmosDbFactory Factory(IServiceProvider provider) => (CosmosDbFactory)provider.GetRequiredService<ICosmosDbFactory>();

    private static ServiceCollection Services(IMemoryCache cache)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryCacheAccessor>(new CacheAccessor(cache));
        services.AddScoped<ISekibanContext, SekibanContext>();
        services.AddSekibanCosmosDbWithoutBlob(Settings(), new SekibanCosmosClientOptions { ClientOptions = ClientOptions() });
        return services;
    }

    private static SekibanCosmosDbOptions Settings() => new()
    {
        Contexts = [Setting(SekibanContext.Default), Setting("Secondary")]
    };

    private static SekibanAzureCosmosDbOption Setting(string context) => new()
    {
        Context = context,
        CosmosEndPointUrl = "https://localhost:8081",
        CosmosAuthorizationKey = Convert.ToBase64String(new byte[64])
    };

    private static CosmosClientOptions ClientOptions() => new()
    {
        ConnectionMode = ConnectionMode.Gateway,
        HttpClientFactory = () => new HttpClient(new NoNetworkHandler())
    };

    private sealed record CacheAccessor(IMemoryCache Cache) : IMemoryCacheAccessor;

    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("Network disabled in lifetime tests"));
    }

    private sealed class TrackingClient : CosmosClient
    {
        public TrackingClient() : base("https://localhost:8081", Convert.ToBase64String(new byte[64]), ClientOptions()) { }
        public int DisposeCount { get; private set; }
        public bool FailDatabaseCreation { get; init; }
        protected override void Dispose(bool disposing)
        {
            DisposeCount++;
            base.Dispose(disposing);
        }
        public override Task<DatabaseResponse> CreateDatabaseIfNotExistsAsync(string id, int? throughput = null,
            RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            FailDatabaseCreation
                ? Task.FromException<DatabaseResponse>(new InvalidOperationException("New client requires its own database handle"))
                : base.CreateDatabaseIfNotExistsAsync(id, throughput, requestOptions, cancellationToken);
    }
}
