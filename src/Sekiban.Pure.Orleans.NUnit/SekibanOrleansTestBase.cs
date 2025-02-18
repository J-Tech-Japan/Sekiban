using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Orleans.TestingHost;
using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Executors;
using Sekiban.Pure.Orleans.Parts;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;
using Sekiban.Pure.Repositories;
namespace Sekiban.Pure.Orleans.NUnit;

[TestFixture]
public abstract class SekibanOrleansTestBase<TDomainTypesGetter> : ISiloConfigurator
    where TDomainTypesGetter : ISiloConfigurator, new()
{
    /// <summary>
    ///     Each test case implements domain types through this abstract property
    /// </summary>
    private SekibanDomainTypes _domainTypes => GetDomainTypes();

    public abstract SekibanDomainTypes GetDomainTypes();

    private ICommandMetadataProvider _commandMetadataProvider;
    private ISekibanExecutor _executor;
    private TestCluster _cluster;
    private Repository _repository = new();

    [SetUp]
    public virtual void SetUp()
    {
        _commandMetadataProvider = new FunctionCommandMetadataProvider(() => "test");
        _repository = new Repository();
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        builder.Options.ClusterId = "dev"; // 固定のクラスタ ID
        builder.Options.ServiceId = "TestService"; // 固定のサービス ID
        builder.AddSiloBuilderConfigurator<TDomainTypesGetter>();
        _cluster = builder.Build();
        _cluster.Deploy();
        _executor = new SekibanOrleansExecutor(_cluster.Client, _domainTypes, _commandMetadataProvider);
    }

    [TearDown]
    public virtual void TearDown()
    {
        _cluster.StopAllSilos();
    }

    /// <summary>
    ///     Execute command in Given phase
    /// </summary>
    protected ResultBox<CommandResponse> GivenCommandWithResult(
        ICommandWithHandlerSerializable command,
        IEvent? relatedEvent = null) =>
        _executor.CommandAsync(command, relatedEvent).UnwrapBox().Result;

    /// <summary>
    ///     Execute command in When phase
    /// </summary>
    protected ResultBox<CommandResponse> WhenCommandWithResult(
        ICommandWithHandlerSerializable command,
        IEvent? relatedEvent = null) =>
        _executor.CommandAsync(command, relatedEvent).UnwrapBox().Result;

    /// <summary>
    ///     Get aggregate in Then phase
    /// </summary>
    protected ResultBox<Aggregate> ThenGetAggregateWithResult<TAggregateProjector>(PartitionKeys partitionKeys)
        where TAggregateProjector : IAggregateProjector, new()
        => _executor.LoadAggregateAsync<TAggregateProjector>(partitionKeys).UnwrapBox().Result;

    protected ResultBox<TResult> ThenQueryWithResult<TResult>(IQueryCommon<TResult> query) where TResult : notnull
        => _executor.QueryAsync(query).UnwrapBox().Result;

    protected ResultBox<ListQueryResult<TResult>> ThenQueryWithResult<TResult>(IListQueryCommon<TResult> query)
        where TResult : notnull
        => _executor.QueryAsync(query).UnwrapBox().Result;

    protected ResultBox<TMultiProjector> ThenGetMultiProjectorWithResult<TMultiProjector>()
        where TMultiProjector : IMultiProjector<TMultiProjector>
    {
        var projector
            = _cluster.Client.GetGrain<IMultiProjectorGrain>(TMultiProjector.GetMultiProjectorName());
        var state = projector.GetStateAsync().Result;
        var typed = _domainTypes.MultiProjectorsType.ToTypedState(state);
        if (typed is MultiProjectionState<TMultiProjector> multiProjectionState)
        {
            return multiProjectionState.Payload;
        }
        return ResultBox<TMultiProjector>.Error(new ApplicationException("Invalid state"));
    }

    /// <summary>
    ///     Execute command in When phase.
    /// </summary>
    protected CommandResponse WhenCommand(
        ICommandWithHandlerSerializable command,
        IEvent? relatedEvent = null) =>
        _executor.CommandAsync(command, relatedEvent).UnwrapBox().Result;

    /// <summary>
    ///     Get aggregate in Then phase.
    /// </summary>
    protected Aggregate ThenGetAggregate<TAggregateProjector>(PartitionKeys partitionKeys)
        where TAggregateProjector : IAggregateProjector, new() =>
        _executor.LoadAggregateAsync<TAggregateProjector>(partitionKeys).UnwrapBox().Result;

    protected TResult ThenQuery<TResult>(IQueryCommon<TResult> query) where TResult : notnull =>
        _executor.QueryAsync(query).UnwrapBox().Result;

    protected ListQueryResult<TResult> ThenQuery<TResult>(IListQueryCommon<TResult> query)
        where TResult : notnull =>
        _executor.QueryAsync(query).UnwrapBox().Result;

    protected TMultiProjector ThenGetMultiProjector<TMultiProjector>()
        where TMultiProjector : IMultiProjector<TMultiProjector>
    {
        var projector = _cluster.Client.GetGrain<IMultiProjectorGrain>(TMultiProjector.GetMultiProjectorName());
        var state = projector.GetStateAsync().Result;
        var typed = _domainTypes.MultiProjectorsType.ToTypedState(state);
        if (typed is MultiProjectionState<TMultiProjector> multiProjectionState)
        {
            return multiProjectionState.Payload;
        }
        throw new ApplicationException("Invalid state");
    }



    public virtual void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder.AddMemoryGrainStorage("PubSubStore");
        siloBuilder.AddMemoryGrainStorageAsDefault();
        siloBuilder.AddMemoryStreams("EventStreamProvider").AddMemoryGrainStorage("EventStreamProvider");
        siloBuilder.ConfigureServices(
            services =>
            {
                services.AddSingleton(_domainTypes);
                services.AddSingleton(_repository);
                services.AddTransient<IEventWriter, InMemoryEventWriter>();
                services.AddTransient<IEventReader, InMemoryEventReader>();
                // services.AddTransient()
            });
    }
}