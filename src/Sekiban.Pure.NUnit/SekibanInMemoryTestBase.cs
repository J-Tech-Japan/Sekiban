using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Executors;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;
using Sekiban.Pure.Repositories;
namespace Sekiban.Pure.NUnit;

[TestFixture]
public abstract class SekibanInMemoryTestBase
{
    /// <summary>
    ///     Each test case implements domain types through this abstract property
    /// </summary>
    private SekibanDomainTypes DomainTypes => GetDomainTypes();
    protected abstract SekibanDomainTypes GetDomainTypes();

    private ICommandMetadataProvider _commandMetadataProvider;
    private IServiceProvider _serviceProvider;
    private ISekibanExecutor _executor;
    protected Repository Repository
    {
        get;
        private set;
    }
    [SetUp]
    public virtual void SetUp()
    {
        _commandMetadataProvider = new FunctionCommandMetadataProvider(() => "test");
        _serviceProvider = new ServiceCollection().BuildServiceProvider();
        Repository = new Repository();
        _executor = new InMemorySekibanExecutor(DomainTypes, _commandMetadataProvider, Repository, _serviceProvider);
    }

    /// <summary>
    ///     Givenフェーズのコマンド実行
    /// </summary>
    protected ResultBox<CommandResponse> GivenCommandWithResult(
        ICommandWithHandlerSerializable command,
        IEvent? relatedEvent = null) =>
        _executor.CommandAsync(command, relatedEvent).Result.UnwrapBox().ToResultBox();

    /// <summary>
    ///     Whenフェーズのコマンド実行
    /// </summary>
    protected ResultBox<CommandResponse> WhenCommandWithResult(
        ICommandWithHandlerSerializable command,
        IEvent? relatedEvent = null) =>
        _executor.CommandAsync(command, relatedEvent).Result.UnwrapBox().ToResultBox();

    /// <summary>
    ///     Thenフェーズの集約取得
    /// </summary>
    protected ResultBox<Aggregate> ThenGetAggregateWithResult<TAggregateProjector>(PartitionKeys partitionKeys)
        where TAggregateProjector : IAggregateProjector, new()
        => _executor.LoadAggregateAsync<TAggregateProjector>(partitionKeys).Result.UnwrapBox().ToResultBox();

    protected ResultBox<TResult> ThenQueryWithResult<TResult>(IQueryCommon<TResult> query) where TResult : notnull
        => _executor.QueryAsync(query).Result.UnwrapBox().ToResultBox();
    protected ResultBox<ListQueryResult<TResult>> ThenQueryWithResult<TResult>(IListQueryCommon<TResult> query)
        where TResult : notnull
        => _executor.QueryAsync(query).Result.UnwrapBox().ToResultBox();

    protected ResultBox<TMultiProjector> ThenGetMultiProjectorWithResult<TMultiProjector>()
        where TMultiProjector : IMultiProjector<TMultiProjector>, new()
        => Repository
            .LoadMultiProjection<TMultiProjector>(MultiProjectionEventSelector.All)
            .Remap(x => x.Payload)
            .Result
            .UnwrapBox()
            .ToResultBox();

    /// <summary>
    ///     Givenフェーズのコマンド実行
    /// </summary>
    protected CommandResponse GivenCommand(
        ICommandWithHandlerSerializable command,
        IEvent? relatedEvent = null) =>
        _executor.CommandAsync(command, relatedEvent).UnwrapBox().Result;

    /// <summary>
    ///     Whenフェーズのコマンド実行
    /// </summary>
    protected CommandResponse WhenCommand(
        ICommandWithHandlerSerializable command,
        IEvent? relatedEvent = null) =>
        _executor.CommandAsync(command, relatedEvent).UnwrapBox().Result;

    /// <summary>
    ///     Thenフェーズの集約取得
    /// </summary>
    protected Aggregate ThenGetAggregate<TAggregateProjector>(PartitionKeys partitionKeys)
        where TAggregateProjector : IAggregateProjector, new()
        => _executor.LoadAggregateAsync<TAggregateProjector>(partitionKeys).UnwrapBox().Result;

    protected TResult ThenQuery<TResult>(IQueryCommon<TResult> query) where TResult : notnull
        => _executor.QueryAsync(query).UnwrapBox().Result;
    protected ListQueryResult<TResult> ThenQuery<TResult>(IListQueryCommon<TResult> query)
        where TResult : notnull
        => _executor.QueryAsync(query).UnwrapBox().Result;

    protected TMultiProjector ThenGetMultiProjector<TMultiProjector>()
        where TMultiProjector : IMultiProjector<TMultiProjector>, new()
        => Repository
            .LoadMultiProjection<TMultiProjector>(MultiProjectionEventSelector.All)
            .Remap(x => x.Payload)
            .UnwrapBox()
            .Result;
}