﻿using Microsoft.Extensions.DependencyInjection;
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
    ///     Abstract property for implementing domain types for each test case
    /// </summary>
    private SekibanDomainTypes DomainTypes => GetDomainTypes();
    protected abstract SekibanDomainTypes GetDomainTypes();

    protected ICommandMetadataProvider CommandMetadataProvider { get; }
        = new FunctionCommandMetadataProvider(() => "test");

    protected IServiceProvider ServiceProvider { get; } = new ServiceCollection().BuildServiceProvider();

    protected Repository Repository { get; } = new();

    protected ISekibanExecutor Executor { get; }

    public SekibanInMemoryTestBase() =>
        Executor = new InMemorySekibanExecutor(DomainTypes, CommandMetadataProvider, Repository, ServiceProvider);

    /// <summary>
    ///     Command execution in Given phase
    /// </summary>
    protected ResultBox<CommandResponse> GivenCommandWithResult(
        ICommandWithHandlerSerializable command,
        IEvent? relatedEvent = null) =>
        Executor.CommandAsync(command, relatedEvent).Result.UnwrapBox().ToResultBox();

    /// <summary>
    ///     Command execution in When phase
    /// </summary>
    protected ResultBox<CommandResponse> WhenCommandWithResult(
        ICommandWithHandlerSerializable command,
        IEvent? relatedEvent = null) =>
        Executor.CommandAsync(command, relatedEvent).Result.UnwrapBox().ToResultBox();

    /// <summary>
    ///     Get aggregate in Then phase
    /// </summary>
    protected ResultBox<Aggregate> ThenGetAggregateWithResult<TAggregateProjector>(PartitionKeys partitionKeys)
        where TAggregateProjector : IAggregateProjector, new() => Executor
        .LoadAggregateAsync<TAggregateProjector>(partitionKeys)
        .Result
        .UnwrapBox()
        .ToResultBox();

    protected ResultBox<TResult> ThenQueryWithResult<TResult>(IQueryCommon<TResult> query) where TResult : notnull =>
        Executor.QueryAsync(query).Result.UnwrapBox().ToResultBox();
    protected ResultBox<ListQueryResult<TResult>> ThenQueryWithResult<TResult>(IListQueryCommon<TResult> query)
        where TResult : notnull => Executor.QueryAsync(query).Result.UnwrapBox().ToResultBox();

    protected ResultBox<TMultiProjector> ThenGetMultiProjectorWithResult<TMultiProjector>()
        where TMultiProjector : IMultiProjector<TMultiProjector>, new() => Repository
        .LoadMultiProjection<TMultiProjector>(MultiProjectionEventSelector.All)
        .Remap(x => x.Payload)
        .Result
        .UnwrapBox()
        .ToResultBox();

    /// <summary>
    ///     Command execution in Given phase
    /// </summary>
    protected CommandResponse GivenCommand(ICommandWithHandlerSerializable command, IEvent? relatedEvent = null) =>
        Executor.CommandAsync(command, relatedEvent).UnwrapBox().Result;

    /// <summary>
    ///     Command execution in When phase
    /// </summary>
    protected CommandResponse WhenCommand(ICommandWithHandlerSerializable command, IEvent? relatedEvent = null) =>
        Executor.CommandAsync(command, relatedEvent).UnwrapBox().Result;

    /// <summary>
    ///     Get aggregate in Then phase
    /// </summary>
    protected Aggregate ThenGetAggregate<TAggregateProjector>(PartitionKeys partitionKeys)
        where TAggregateProjector : IAggregateProjector, new() =>
        Executor.LoadAggregateAsync<TAggregateProjector>(partitionKeys).UnwrapBox().Result;

    protected TResult ThenQuery<TResult>(IQueryCommon<TResult> query) where TResult : notnull =>
        Executor.QueryAsync(query).UnwrapBox().Result;
    protected ListQueryResult<TResult> ThenQuery<TResult>(IListQueryCommon<TResult> query) where TResult : notnull =>
        Executor.QueryAsync(query).UnwrapBox().Result;

    protected TMultiProjector ThenGetMultiProjector<TMultiProjector>()
        where TMultiProjector : IMultiProjector<TMultiProjector>, new() => Repository
        .LoadMultiProjection<TMultiProjector>(MultiProjectionEventSelector.All)
        .Remap(x => x.Payload)
        .UnwrapBox()
        .Result;

}
