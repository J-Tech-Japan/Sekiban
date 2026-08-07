using Dcb.Domain;
using Dcb.Domain.Enrollment;
using Dcb.Domain.Student;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Streams;
using Orleans.TestingHost;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using System.Text.Json;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     SEK-G23 executed-user provider acceptance tests for the Orleans executor path.
/// </summary>
public class ExecutedUserProviderTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;
    private readonly DcbDomainTypes _domainTypes = CreateDomainTypes();
    private readonly InMemoryEventStore _eventStore = new(CreateDomainTypes().EventTypes);

    public async Task InitializeAsync()
    {
        ProviderSiloConfigurator.EventStore = _eventStore;
        ProviderSiloConfigurator.DomainTypes = _domainTypes;
        ProviderSiloConfigurator.Provider = null;

        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        builder.Options.ClusterId = $"TestCluster-{uniqueId}";
        builder.Options.ServiceId = $"TestService-{uniqueId}";
        builder.AddSiloBuilderConfigurator<ProviderSiloConfigurator>();
        builder.AddClientBuilderConfigurator<TestClientConfigurator>();

        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        await _cluster.StopAllSilosAsync();
        _cluster.Dispose();
    }

    [Fact]
    public async Task Orleans_Provider_Registered_In_Same_ServiceProvider_Is_Applied()
    {
        var provider = new ConstantProvider("orleans-admin@contoso.com");
        SetProvider(provider);

        var executor = CreateExecutorFromSiloServices();
        var studentId = Guid.NewGuid();
        var result = await executor.ExecuteAsync(new CreateStudent(studentId, "Orleans Student", 5));
        Assert.True(result.IsSuccess);

        var events = (await _eventStore.ReadAllSerializableEventsAsync()).GetValue().ToList();
        Assert.Single(events);
        Assert.Equal("orleans-admin@contoso.com", events[0].EventMetadata.ExecutedUser);
    }

    [Fact]
    public async Task Orleans_Provider_In_Unseen_Container_Is_Not_Applied()
    {
        SetProvider(null);

        var unseenServices = new ServiceCollection();
        unseenServices.AddSingleton<IExecutedUserProvider>(new ConstantProvider("unseen-user"));
        var unseenSp = unseenServices.BuildServiceProvider();

        var executor = ActivatorUtilities.CreateInstance<OrleansDcbExecutor>(
            GetSiloServiceProvider(),
            _cluster.Client,
            _eventStore,
            _domainTypes);

        var studentId = Guid.NewGuid();
        var result = await executor.ExecuteAsync(new CreateStudent(studentId, "Orleans Student", 5));
        Assert.True(result.IsSuccess);

        var events = (await _eventStore.ReadAllSerializableEventsAsync()).GetValue().ToList();
        Assert.Single(events);
        Assert.Equal("GeneralSekibanExecutor", events[0].EventMetadata.ExecutedUser);
    }

    private void SetProvider(IExecutedUserProvider? provider)
    {
        ProviderSiloConfigurator.Provider = provider;
    }

    private OrleansDcbExecutor CreateExecutorFromSiloServices()
    {
        var siloSp = GetSiloServiceProvider();
        return ActivatorUtilities.CreateInstance<OrleansDcbExecutor>(
            siloSp,
            _cluster.Client,
            _eventStore,
            _domainTypes);
    }

    private IServiceProvider GetSiloServiceProvider() =>
        ((InProcessSiloHandle)_cluster.Silos[0]).ServiceProvider;

    private static DcbDomainTypes CreateDomainTypes()
    {
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<StudentCreated>();
        eventTypes.RegisterEventType<StudentEnrolledInClassRoom>();
        eventTypes.RegisterEventType<StudentDroppedFromClassRoom>();
        var tagTypes = new SimpleTagTypes();
        var tagProjectorTypes = new SimpleTagProjectorTypes();
        tagProjectorTypes.RegisterProjector<StudentProjector>();
        var tagStatePayloadTypes = new SimpleTagStatePayloadTypes();
        tagStatePayloadTypes.RegisterPayloadType<StudentState>();
        var multiProjectorTypes = new SimpleMultiProjectorTypes();
        multiProjectorTypes.RegisterProjector<GenericTagMultiProjector<StudentProjector, StudentTag>>();
        var queryTypes = new SimpleQueryTypes();
        return new DcbDomainTypes(
            eventTypes,
            tagTypes,
            tagProjectorTypes,
            tagStatePayloadTypes,
            multiProjectorTypes,
            queryTypes,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private sealed class ConstantProvider : IExecutedUserProvider
    {
        private readonly string _value;
        public ConstantProvider(string value) => _value = value;
        public string GetExecutedUser() => _value;
    }

    private sealed class ConfigurableProvider : IExecutedUserProvider
    {
        public string GetExecutedUser() => ProviderSiloConfigurator.Provider?.GetExecutedUser() ?? string.Empty;
    }

    public class ProviderSiloConfigurator : ISiloConfigurator
    {
        internal static IEventStore? EventStore;
        internal static DcbDomainTypes? DomainTypes;
        internal static IExecutedUserProvider? Provider;

        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .ConfigureServices(services =>
                {
                    services.AddSingleton(DomainTypes ?? throw new InvalidOperationException("DomainTypes not set"));
                    services.AddSingleton(EventStore ?? throw new InvalidOperationException("EventStore not set"));
                    services.AddSingleton<IMultiProjectionStateStore, InMemoryMultiProjectionStateStore>();
                    services.AddSingleton<IEventSubscriptionResolver>(
                        new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
                    services.AddSingleton<IActorObjectAccessor, OrleansActorObjectAccessor>();
                    services.AddSingleton<IBlobStorageSnapshotAccessor, MockBlobStorageSnapshotAccessor>();
                    services.AddTransient<IMultiProjectionEventStatistics, NoOpMultiProjectionEventStatistics>();
                    services.AddTransient<GeneralMultiProjectionActorOptions>(_ => new GeneralMultiProjectionActorOptions
                    {
                        SafeWindowMs = 20000
                    });
                    services.AddSingleton<IExecutedUserProvider, ConfigurableProvider>();

                    services.AddSekibanDcbNativeRuntime();
                })
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryGrainStorage("OrleansStorage")
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryStreams("EventStreamProvider")
                .AddMemoryGrainStorage("EventStreamProvider");
        }
    }

    public class TestClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            clientBuilder.AddMemoryStreams("EventStreamProvider");
        }
    }
}
