using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Dependency;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.MultipleProjections;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Testing.Command;
using Sekiban.Testing.Queries;
namespace Sekiban.Testing.Projection;

public abstract class MultipleProjectionsAndQueriesTestBase<TDependencyDefinition> where TDependencyDefinition : IDependencyDefinition, new()
{
    private readonly TestCommandExecutor _commandExecutor;
    protected readonly IServiceProvider _serviceProvider;

    // ReSharper disable once PublicConstructorInAbstractClass
    public MultipleProjectionsAndQueriesTestBase()
    {
        var services = new ServiceCollection();
        // ReSharper disable once VirtualMemberCallInConstructor
        SetupDependency(services);
        services.AddQueriesFromDependencyDefinition(new TDependencyDefinition());
        services.AddSekibanCoreForAggregateTestWithDependency(new TDependencyDefinition());
        _serviceProvider = services.BuildServiceProvider();
        _commandExecutor = new TestCommandExecutor(_serviceProvider);
    }
    protected virtual void SetupDependency(IServiceCollection serviceCollection) { }

    public TMultipleProjectionTest SetupMultiProjectionTest<TMultipleProjectionTest>()
        where TMultipleProjectionTest : class, IMultiProjectTestBase
    {
        var test = Activator.CreateInstance(typeof(TMultipleProjectionTest), _serviceProvider) as TMultipleProjectionTest;
        if (test is null) { throw new InvalidOperationException("Could not create test"); }
        return test;
    }

    public Guid RunCreateCommand<TAggregatePayload>(ICreateCommand<TAggregatePayload> command, Guid? injectingAggregateId = null)
        where TAggregatePayload : IAggregatePayload, new()
    {
        var (events, aggregateId) = _commandExecutor.ExecuteCreateCommand(command, injectingAggregateId);
        return aggregateId;
    }
    public void RunChangeCommand<TAggregatePayload>(ChangeCommandBase<TAggregatePayload> command)
        where TAggregatePayload : IAggregatePayload, new()
    {
        var events = _commandExecutor.ExecuteChangeCommand(command);
    }
    public MultipleProjectionsAndQueriesTestBase<TDependencyDefinition> GivenCommandExecutorAction(Action<TestCommandExecutor> action)
    {
        action(_commandExecutor);
        return this;
    }

    public MultipleProjectionsAndQueriesTestBase<TDependencyDefinition> GivenScenario(Action initialAction)
    {
        initialAction();
        return this;
    }

    public MultipleProjectionsAndQueriesTestBase<TDependencyDefinition> GivenQueryChecker(
        IQueryChecker checker)
    {
        if (_serviceProvider is null) { throw new Exception("Service provider is null. Please setup service provider."); }
        checker.QueryService = _serviceProvider.GetService<IQueryService>();
        return this;
    }

    public MultipleProjectionsAndQueriesTestBase<TDependencyDefinition> GetMultipleProjectionTest<TProjection, TProjectionPayload>(
        Action<MultiProjectionMultiProjectTestBase<TProjection, TProjectionPayload, TDependencyDefinition>> testAction)
        where TProjection : MultiProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new()
    {
        var test = SetupMultiProjectionTest<MultiProjectionMultiProjectTestBase<TProjection, TProjectionPayload, TDependencyDefinition>>();
        testAction(test);
        return this;
    }
    public MultipleProjectionsAndQueriesTestBase<TDependencyDefinition> GetAggregateListProjectionTest<TAggregatePayload>(
        Action<AggregateListProjectionMultiProjectTestBase<TAggregatePayload, TDependencyDefinition>> testAction)
        where TAggregatePayload : IAggregatePayload, new()
    {
        var test = SetupMultiProjectionTest<AggregateListProjectionMultiProjectTestBase<TAggregatePayload, TDependencyDefinition>>();
        testAction(test);
        return this;
    }
    public MultipleProjectionsAndQueriesTestBase<TDependencyDefinition> GetSingleProjectionListProjectionTest<TAggregatePayload, TSingleProjection,
        TSingleProjectionPayload>(
        Action<SingleProjectionListMultiProjectTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload, TDependencyDefinition>>
            testAction)
        where TAggregatePayload : IAggregatePayload, new()
        where TSingleProjection : MultiProjectionBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>, new()
        where TSingleProjectionPayload : ISingleProjectionPayload
    {
        var test =
            SetupMultiProjectionTest<SingleProjectionListMultiProjectTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
                TDependencyDefinition>>();
        testAction(test);
        return this;
    }

    public MultipleProjectionsAndQueriesTestBase<TDependencyDefinition> GetMultiProjectionQueryTest<TProjection, TProjectionPayload, TQuery,
        TQueryParameter, TQueryResponse>(
        Action<MultiProjectionQueryTest<TProjection, TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>> queryTestAction)
        where TProjection : MultiProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new()
        where TQueryParameter : IQueryParameter
        where TQuery : IMultiProjectionQuery<TProjection, TProjectionPayload, TQueryParameter, TQueryResponse>, new()
    {
        var queryChecker = new MultiProjectionQueryTest<TProjection, TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>();
        GivenQueryChecker(queryChecker);
        queryTestAction(queryChecker);
        return this;
    }
    public MultipleProjectionsAndQueriesTestBase<TDependencyDefinition> GetMultiProjectionListQueryTest<TProjection, TProjectionPayload, TQuery,
        TQueryParameter, TQueryResponse>(
        Action<MultiProjectionListQueryTest<TProjection, TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>> queryTestAction)
        where TProjection : MultiProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new()
        where TQueryParameter : IQueryParameter
        where TQuery : IMultiProjectionListQuery<TProjection, TProjectionPayload, TQueryParameter, TQueryResponse>, new()
    {
        var queryChecker = new MultiProjectionListQueryTest<TProjection, TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>();
        GivenQueryChecker(queryChecker);
        queryTestAction(queryChecker);
        return this;
    }


    public MultipleProjectionsAndQueriesTestBase<TDependencyDefinition> GetAggregateQueryTest<TAggregatePayload, TQuery, TQueryParameter,
        TQueryResponse>(
        Action<AggregateQueryTest<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>> queryTestAction)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var queryChecker = new AggregateQueryTest<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>();
        GivenQueryChecker(queryChecker);
        queryTestAction(queryChecker);
        return this;
    }
    public MultipleProjectionsAndQueriesTestBase<TDependencyDefinition> GetAggregateListQueryTest<TAggregatePayload, TQuery, TQueryParameter,
        TQueryResponse>(
        Action<AggregateListQueryTest<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>> queryTestAction)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var queryChecker = new AggregateListQueryTest<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>();
        GivenQueryChecker(queryChecker);
        queryTestAction(queryChecker);
        return this;
    }

    public MultipleProjectionsAndQueriesTestBase<TDependencyDefinition> GetSingleProjectionQueryTest<TAggregatePayload, TSingleProjection,
        TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
        Action<SingleProjectionQueryTest<TAggregatePayload, TSingleProjection, TSingleProjectionPayload, TQuery, TQueryParameter,
            TQueryResponse>> queryTestAction)
        where TAggregatePayload : IAggregatePayload, new()
        where TSingleProjection : MultiProjectionBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>, new()
        where TSingleProjectionPayload : ISingleProjectionPayload
        where TQuery : ISingleProjectionQuery<TAggregatePayload, TSingleProjection,
            TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var queryChecker = new SingleProjectionQueryTest<TAggregatePayload, TSingleProjection,
            TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>();
        GivenQueryChecker(queryChecker);
        queryTestAction(queryChecker);
        return this;
    }
    public MultipleProjectionsAndQueriesTestBase<TDependencyDefinition> GetSingleProjectionListQueryTest<TAggregatePayload, TSingleProjection,
        TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
        Action<SingleProjectionListQueryTest<TAggregatePayload, TSingleProjection, TSingleProjectionPayload, TQuery, TQueryParameter,
            TQueryResponse>> queryTestAction)
        where TAggregatePayload : IAggregatePayload, new()
        where TSingleProjection : MultiProjectionBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>, new()
        where TSingleProjectionPayload : ISingleProjectionPayload
        where TQuery : ISingleProjectionListQuery<TAggregatePayload, TSingleProjection,
            TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var queryChecker = new SingleProjectionListQueryTest<TAggregatePayload, TSingleProjection,
            TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>();
        GivenQueryChecker(queryChecker);
        queryTestAction(queryChecker);
        return this;
    }


    public AggregateState<TEnvironmentAggregatePayload> GetAggregateState<TEnvironmentAggregate, TEnvironmentAggregatePayload>(
        Guid aggregateId)
        where TEnvironmentAggregatePayload : IAggregatePayload, new()
    {
        var singleProjectionService = _serviceProvider.GetRequiredService(typeof(ISingleProjectionService)) as ISingleProjectionService;
        if (singleProjectionService is null) { throw new Exception("Failed to get single aggregate service"); }
        var aggregate = singleProjectionService.GetAggregateStateAsync<TEnvironmentAggregatePayload>(aggregateId).Result;
        return aggregate ?? throw new SekibanAggregateNotExistsException(aggregateId, typeof(TEnvironmentAggregate).Name);
    }
    public IReadOnlyCollection<IEvent> GetLatestEvents() => _commandExecutor.LatestEvents;
}
