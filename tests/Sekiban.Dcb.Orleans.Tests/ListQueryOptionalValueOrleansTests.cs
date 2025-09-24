using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tests;
using Xunit;
using Xunit.Sdk;
namespace Sekiban.Dcb.Orleans.Tests;

public class ListQueryOptionalValueOrleansTests : IAsyncLifetime
{
    private static IEventStore SharedEventStore = new InMemoryEventStore();
    private TestCluster _cluster = null!;
    private DcbDomainTypes _domainTypes = null!;
    private IEventStore _eventStore = null!;
    private ISekibanExecutor _executor = null!;
    private bool _initialized;

    public async Task InitializeAsync()
    {
        SharedEventStore = new InMemoryEventStore();

        var builder = new TestClusterBuilder
        {
            Options =
            {
                InitialSilosCount = 1
            }
        };
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        builder.Options.ClusterId = $"OptionalDateCluster-{uniqueId}";
        builder.Options.ServiceId = $"OptionalDateService-{uniqueId}";
        builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<TestClientConfigurator>();

        _cluster = builder.Build();
        await _cluster.DeployAsync();

        _domainTypes = CreateDomainTypes();
        _eventStore = SharedEventStore;
        _executor = new OrleansDcbExecutor(_cluster.Client, _eventStore, _domainTypes);
        _initialized = true;
    }

    public async Task DisposeAsync()
    {
        if (_cluster != null)
        {
            await _cluster.StopAllSilosAsync();
            _cluster.Dispose();
        }
    }

    [Fact]
    public async Task OrleansExecutor_Should_Return_Items_With_OptionalValue()
    {
        await EnsureInitializedAsync();

        var query = new OptionalDateListQuery();
        var result = await _executor.QueryAsync(query);

        if (!result.IsSuccess)
        {
            throw new XunitException(result.GetException()?.ToString() ?? "List query failed without exception detail");
        }

        var items = result.GetValue().Items.ToList();
        Assert.Equal(OptionalDateFixtures.SeedResults.Count, items.Count);
        Assert.Contains(items, item => item.Date.HasValue && item.Date.GetValue() == OptionalDateFixtures.ExpectedDate);
        Assert.Contains(items, item => !item.Date.HasValue);
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized && _executor is not null)
        {
            return;
        }

        if (_cluster == null)
        {
            var builder = new TestClusterBuilder
            {
                Options =
                {
                    InitialSilosCount = 1
                }
            };
            var uniqueId = Guid.NewGuid().ToString("N")[..8];
            builder.Options.ClusterId = $"FallbackOptionalDateCluster-{uniqueId}";
            builder.Options.ServiceId = $"FallbackOptionalDateService-{uniqueId}";
            builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
            builder.AddClientBuilderConfigurator<TestClientConfigurator>();
            _cluster = builder.Build();
            await _cluster.DeployAsync();
        }

        if (_executor == null)
        {
            _domainTypes = CreateDomainTypes();
            _eventStore = SharedEventStore;
            _executor = new OrleansDcbExecutor(_cluster.Client, _eventStore, _domainTypes);
        }

        _initialized = true;
    }

    private static DcbDomainTypes CreateDomainTypes() =>
        DcbDomainTypes.Simple(types =>
        {
            types.MultiProjectorTypes.RegisterProjector<TestOptionalDateMultiProjector>();
            types.QueryTypes.RegisterListQuery<OptionalDateListQuery>();
        });

    private class TestSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IEventStore>(_ => SharedEventStore);
                    services.AddSingleton<DcbDomainTypes>(_ => CreateDomainTypes());
                    services.AddSingleton<IEventSubscriptionResolver>(
                        new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
                    services.AddSingleton<IActorObjectAccessor, OrleansActorObjectAccessor>();
                    services.AddSingleton<IBlobStorageSnapshotAccessor, MockBlobStorageSnapshotAccessor>();
                    services.AddTransient<IMultiProjectionEventStatistics, Sekiban.Dcb.MultiProjections.NoOpMultiProjectionEventStatistics>();
                    services.AddTransient(_ => new GeneralMultiProjectionActorOptions
                    {
                        SafeWindowMs = 20000,
                        SnapshotOffloadThresholdBytes = 2 * 1024 * 1024
                    });
                })
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryGrainStorage("OrleansStorage")
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryStreams("EventStreamProvider")
                .AddMemoryGrainStorage("EventStreamProvider");
        }
    }

    private class TestClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            clientBuilder.AddMemoryStreams("EventStreamProvider");
        }
    }
}