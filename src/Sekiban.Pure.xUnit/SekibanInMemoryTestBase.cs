using Microsoft.Extensions.DependencyInjection;
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
namespace Sekiban.Pure.xUnit;

public abstract class SekibanInMemoryTestBase
{
    /// <summary>
    ///     各テストケースごとにドメインタイプを実装するための抽象プロパティ
    /// </summary>
    private SekibanDomainTypes DomainTypes => GetDomainTypes();
    protected abstract SekibanDomainTypes GetDomainTypes();

    protected ICommandMetadataProvider CommandMetadataProvider { get; }
        = new FunctionCommandMetadataProvider(() => "test");

    protected IServiceProvider ServiceProvider { get; }
        = new ServiceCollection().BuildServiceProvider();

    protected Repository Repository { get; } = new();

    protected ISekibanExecutor Executor { get; }

    public SekibanInMemoryTestBase() =>
        Executor = new InMemorySekibanExecutor(DomainTypes, CommandMetadataProvider, Repository, ServiceProvider);

    /// <summary>
    ///     Givenフェーズのコマンド実行
    /// </summary>
    protected Task<ResultBox<CommandResponse>> GivenCommand(
        ICommandWithHandlerSerializable command,
        IEvent? relatedEvent = null) =>
        Executor.CommandAsync(command, relatedEvent);

    /// <summary>
    ///     Whenフェーズのコマンド実行
    /// </summary>
    protected Task<ResultBox<CommandResponse>> WhenCommand(
        ICommandWithHandlerSerializable command,
        IEvent? relatedEvent = null) =>
        Executor.CommandAsync(command, relatedEvent);

    /// <summary>
    ///     Thenフェーズの集約取得
    /// </summary>
    protected Task<ResultBox<Aggregate>> ThenGetAggregate<TAggregateProjector>(PartitionKeys partitionKeys)
        where TAggregateProjector : IAggregateProjector, new()
        => Executor.LoadAggregateAsync<TAggregateProjector>(partitionKeys);

    protected Task<ResultBox<TResult>> ThenQuery<TResult>(IQueryCommon<TResult> query) where TResult : notnull
        => Executor.QueryAsync(query);
    protected Task<ResultBox<ListQueryResult<TResult>>> ThenQuery<TResult>(IListQueryCommon<TResult> query)
        where TResult : notnull
        => Executor.QueryAsync(query);

    protected Task<ResultBox<TMultiProjector>> ThenGetMultiProjector<TMultiProjector>()
        where TMultiProjector : IMultiProjector<TMultiProjector>, new()
        => Repository.LoadMultiProjection<TMultiProjector>(MultiProjectionEventSelector.All).Remap(x => x.Payload);
}