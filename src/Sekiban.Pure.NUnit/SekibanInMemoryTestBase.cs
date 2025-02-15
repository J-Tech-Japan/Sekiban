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
    ///     Execute command in Given phase
    /// </summary>
    protected Task<ResultBox<CommandResponse>> GivenCommand(
        ICommandWithHandlerSerializable command,
        IEvent? relatedEvent = null) =>
        _executor.CommandAsync(command, relatedEvent);

    /// <summary>
    ///     Execute command in When phase
    /// </summary>
    protected Task<ResultBox<CommandResponse>> WhenCommand(
        ICommandWithHandlerSerializable command,
        IEvent? relatedEvent = null) =>
        _executor.CommandAsync(command, relatedEvent);

    /// <summary>
    ///     Get aggregate in Then phase
    /// </summary>
    protected Task<ResultBox<Aggregate>> ThenGetAggregate<TAggregateProjector>(PartitionKeys partitionKeys)
        where TAggregateProjector : IAggregateProjector, new()
        => _executor.LoadAggregateAsync<TAggregateProjector>(partitionKeys);

    protected Task<ResultBox<TResult>> ThenQuery<TResult>(IQueryCommon<TResult> query) where TResult : notnull
        => _executor.QueryAsync(query);

    protected Task<ResultBox<ListQueryResult<TResult>>> ThenQuery<TResult>(IListQueryCommon<TResult> query)
        where TResult : notnull
        => _executor.QueryAsync(query);

    protected Task<ResultBox<TMultiProjector>> ThenGetMultiProjector<TMultiProjector>()
        where TMultiProjector : IMultiProjector<TMultiProjector>, new()
        => Repository.LoadMultiProjection<TMultiProjector>(MultiProjectionEventSelector.All).Remap(x => x.Payload);
}