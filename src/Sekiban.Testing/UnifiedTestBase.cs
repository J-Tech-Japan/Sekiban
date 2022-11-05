using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Dependency;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Testing.Command;
using Sekiban.Testing.Projection;
using Sekiban.Testing.Queries;
using Sekiban.Testing.SingleProjections;
namespace Sekiban.Testing;

public abstract class UnifiedTestBase<TDependencyDefinition> where TDependencyDefinition : IDependencyDefinition, new()
{
    private readonly TestCommandExecutor _commandExecutor;
    protected readonly IServiceProvider _serviceProvider;

    // ReSharper disable once PublicConstructorInAbstractClass
    public UnifiedTestBase()
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

    public TMultiProjectionTest SetupMultiProjectionTest<TMultiProjectionTest>()
        where TMultiProjectionTest : class, IMultiProjectTestBase
    {
        var test = Activator.CreateInstance(typeof(TMultiProjectionTest), _serviceProvider) as TMultiProjectionTest;
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
    public UnifiedTestBase<TDependencyDefinition> GivenCommandExecutorAction(Action<TestCommandExecutor> action)
    {
        action(_commandExecutor);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> GivenScenario(Action initialAction)
    {
        initialAction();
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> GivenQueryTest(
        IQueryTest test)
    {
        if (_serviceProvider is null) { throw new Exception("Service provider is null. Please setup service provider."); }
        test.QueryService = _serviceProvider.GetService<IQueryService>();
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenGetMultiProjectionTest<TProjection, TProjectionPayload>(
        Action<MultiProjectionTestBase<TProjection, TProjectionPayload, TDependencyDefinition>> testAction)
        where TProjection : MultiProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new()
    {
        var test = SetupMultiProjectionTest<MultiProjectionTestBase<TProjection, TProjectionPayload, TDependencyDefinition>>();
        testAction(test);
        return this;
    }
    public MultiProjectionTestBase<TProjection, TProjectionPayload, TDependencyDefinition> GetMultiProjectionTest<TProjection, TProjectionPayload>()
        where TProjection : MultiProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new() =>
        SetupMultiProjectionTest<MultiProjectionTestBase<TProjection, TProjectionPayload, TDependencyDefinition>>();
    public UnifiedTestBase<TDependencyDefinition> ThenGetAggregateListProjectionTest<TAggregatePayload>(
        Action<AggregateListProjectionTestBase<TAggregatePayload, TDependencyDefinition>> testAction)
        where TAggregatePayload : IAggregatePayload, new()
    {
        var test = SetupMultiProjectionTest<AggregateListProjectionTestBase<TAggregatePayload, TDependencyDefinition>>();
        testAction(test);
        return this;
    }
    public AggregateListProjectionTestBase<TAggregatePayload, TDependencyDefinition> GetAggregateListProjectionTest<TAggregatePayload>()
        where TAggregatePayload : IAggregatePayload, new() =>
        SetupMultiProjectionTest<AggregateListProjectionTestBase<TAggregatePayload, TDependencyDefinition>>();
    public UnifiedTestBase<TDependencyDefinition> ThenGetSingleProjectionListProjectionTest<TAggregatePayload, TSingleProjection,
        TSingleProjectionPayload>(
        Action<SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload, TDependencyDefinition>>
            testAction)
        where TAggregatePayload : IAggregatePayload, new()
        where TSingleProjection : SingleProjectionBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>, new()
        where TSingleProjectionPayload : ISingleProjectionPayload
    {
        var test =
            SetupMultiProjectionTest<SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
                TDependencyDefinition>>();
        testAction(test);
        return this;
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload, TDependencyDefinition>
        GetSingleProjectionListProjectionTest<TAggregatePayload, TSingleProjection,
            TSingleProjectionPayload>()
        where TAggregatePayload : IAggregatePayload, new()
        where TSingleProjection : SingleProjectionBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>, new()
        where TSingleProjectionPayload : ISingleProjectionPayload =>
        SetupMultiProjectionTest<SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
            TDependencyDefinition>>();

    public UnifiedTestBase<TDependencyDefinition> ThenGetAggregateTest<TAggregatePayload>(
        Action<AggregateTestBase<TAggregatePayload, TDependencyDefinition>> aggregateTestAction) where TAggregatePayload : IAggregatePayload, new()
    {
        var test = new AggregateTestBase<TAggregatePayload, TDependencyDefinition>(_serviceProvider);
        aggregateTestAction(test);
        return this;
    }
    public AggregateTestBase<TAggregatePayload, TDependencyDefinition> GetAggregateTest<TAggregatePayload>()
        where TAggregatePayload : IAggregatePayload, new() => new(_serviceProvider);
    public UnifiedTestBase<TDependencyDefinition> ThenGetAggregateTestFoeExistingAggregate<TAggregatePayload>(
        Guid aggregateId,
        Action<AggregateTestBase<TAggregatePayload, TDependencyDefinition>> aggregateTestAction) where TAggregatePayload : IAggregatePayload, new()
    {
        var test = new AggregateTestBase<TAggregatePayload, TDependencyDefinition>(_serviceProvider, aggregateId);
        aggregateTestAction(test);
        return this;
    }
    public AggregateTestBase<TAggregatePayload, TDependencyDefinition> GetAggregateTestFoeExistingAggregate<TAggregatePayload>(Guid aggregateId)
        where TAggregatePayload : IAggregatePayload, new() => new(_serviceProvider, aggregateId);
    public UnifiedTestBase<TDependencyDefinition> ThenGetMultiProjectionQueryTest<TProjection, TProjectionPayload, TQuery,
        TQueryParameter, TQueryResponse>(
        Action<MultiProjectionQueryTest<TProjection, TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>> queryTestAction)
        where TProjection : MultiProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new()
        where TQueryParameter : IQueryParameter
        where TQuery : IMultiProjectionQuery<TProjection, TProjectionPayload, TQueryParameter, TQueryResponse>, new()
    {
        var queryTest = new MultiProjectionQueryTest<TProjection, TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>();
        GivenQueryTest(queryTest);
        queryTestAction(queryTest);
        return this;
    }
    public MultiProjectionQueryTest<TProjection, TProjectionPayload, TQuery, TQueryParameter, TQueryResponse> GetMultiProjectionQueryTest<TProjection,
        TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>() where TProjection : MultiProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new()
        where TQueryParameter : IQueryParameter
        where TQuery : IMultiProjectionQuery<TProjection, TProjectionPayload, TQueryParameter, TQueryResponse>, new()
    {
        var queryTest = new MultiProjectionQueryTest<TProjection, TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>();
        GivenQueryTest(queryTest);
        return queryTest;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenGetMultiProjectionListQueryTest<TProjection, TProjectionPayload, TQuery,
        TQueryParameter, TQueryResponse>(
        Action<MultiProjectionListQueryTest<TProjection, TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>> queryTestAction)
        where TProjection : MultiProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new()
        where TQueryParameter : IQueryParameter
        where TQuery : IMultiProjectionListQuery<TProjection, TProjectionPayload, TQueryParameter, TQueryResponse>, new()
    {
        var queryTest = new MultiProjectionListQueryTest<TProjection, TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>();
        GivenQueryTest(queryTest);
        queryTestAction(queryTest);
        return this;
    }
    public MultiProjectionListQueryTest<TProjection, TProjectionPayload, TQuery, TQueryParameter, TQueryResponse> GetMultiProjectionListQueryTest<
        TProjection, TProjectionPayload, TQuery,
        TQueryParameter, TQueryResponse>()
        where TProjection : MultiProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new()
        where TQueryParameter : IQueryParameter
        where TQuery : IMultiProjectionListQuery<TProjection, TProjectionPayload, TQueryParameter, TQueryResponse>, new()
    {
        var queryTest = new MultiProjectionListQueryTest<TProjection, TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>();
        GivenQueryTest(queryTest);
        return queryTest;
    }


    public UnifiedTestBase<TDependencyDefinition> ThenGetAggregateQueryTest<TAggregatePayload, TQuery, TQueryParameter,
        TQueryResponse>(
        Action<AggregateQueryTest<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>> queryTestAction)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var queryTest = new AggregateQueryTest<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>();
        GivenQueryTest(queryTest);
        queryTestAction(queryTest);
        return this;
    }
    public AggregateQueryTest<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse> GetAggregateQueryTest<TAggregatePayload, TQuery,
        TQueryParameter, TQueryResponse>() where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var queryTest = new AggregateQueryTest<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>();
        GivenQueryTest(queryTest);
        return queryTest;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenGetAggregateListQueryTest<TAggregatePayload, TQuery, TQueryParameter,
        TQueryResponse>(
        Action<AggregateListQueryTest<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>> queryTestAction)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var queryTest = new AggregateListQueryTest<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>();
        GivenQueryTest(queryTest);
        queryTestAction(queryTest);
        return this;
    }
    public AggregateListQueryTest<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse> GetAggregateListQueryTest<TAggregatePayload, TQuery,
        TQueryParameter, TQueryResponse>() where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var queryTest = new AggregateListQueryTest<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>();
        GivenQueryTest(queryTest);
        return queryTest;
    }


    public UnifiedTestBase<TDependencyDefinition> ThenGetSingleProjectionQueryTest<TAggregatePayload, TSingleProjection,
        TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
        Action<SingleProjectionQueryTest<TAggregatePayload, TSingleProjection, TSingleProjectionPayload, TQuery, TQueryParameter,
            TQueryResponse>> queryTestAction)
        where TAggregatePayload : IAggregatePayload, new()
        where TSingleProjection : SingleProjectionBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>, new()
        where TSingleProjectionPayload : ISingleProjectionPayload
        where TQuery : ISingleProjectionQuery<TAggregatePayload, TSingleProjection,
            TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var queryTest = new SingleProjectionQueryTest<TAggregatePayload, TSingleProjection,
            TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>();
        GivenQueryTest(queryTest);
        queryTestAction(queryTest);
        return this;
    }
    public SingleProjectionQueryTest<TAggregatePayload, TSingleProjection, TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse> GetSingleProjectionQueryTest<TAggregatePayload, TSingleProjection,
        TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>()
        where TAggregatePayload : IAggregatePayload, new()
        where TSingleProjection : SingleProjectionBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>, new()
        where TSingleProjectionPayload : ISingleProjectionPayload
        where TQuery : ISingleProjectionQuery<TAggregatePayload, TSingleProjection,
            TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var queryTest = new SingleProjectionQueryTest<TAggregatePayload, TSingleProjection,
            TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>();
        GivenQueryTest(queryTest);
        return queryTest;
    }
    public UnifiedTestBase<TDependencyDefinition> ThenGetSingleProjectionListQueryTest<TAggregatePayload, TSingleProjection,
        TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
        Action<SingleProjectionListQueryTest<TAggregatePayload, TSingleProjection, TSingleProjectionPayload, TQuery, TQueryParameter,
            TQueryResponse>> queryTestAction)
        where TAggregatePayload : IAggregatePayload, new()
        where TSingleProjection : SingleProjectionBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>, new()
        where TSingleProjectionPayload : ISingleProjectionPayload
        where TQuery : ISingleProjectionListQuery<TAggregatePayload, TSingleProjection,
            TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var queryTest = new SingleProjectionListQueryTest<TAggregatePayload, TSingleProjection,
            TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>();
        GivenQueryTest(queryTest);
        queryTestAction(queryTest);
        return this;
    }
    public SingleProjectionListQueryTest<TAggregatePayload, TSingleProjection, TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse> GetSingleProjectionListQueryTest<TAggregatePayload, TSingleProjection,
        TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>()
        where TAggregatePayload : IAggregatePayload, new()
        where TSingleProjection : SingleProjectionBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>, new()
        where TSingleProjectionPayload : ISingleProjectionPayload
        where TQuery : ISingleProjectionListQuery<TAggregatePayload, TSingleProjection,
            TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var queryTest = new SingleProjectionListQueryTest<TAggregatePayload, TSingleProjection,
            TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>();
        GivenQueryTest(queryTest);
        return queryTest;
    }
    public AggregateState<TEnvironmentAggregatePayload> GetAggregateState<TEnvironmentAggregatePayload>(
        Guid aggregateId)
        where TEnvironmentAggregatePayload : IAggregatePayload, new()
    {
        var singleProjectionService = _serviceProvider.GetRequiredService(typeof(ISingleProjectionService)) as ISingleProjectionService;
        if (singleProjectionService is null) { throw new Exception("Failed to get single aggregate service"); }
        var aggregate = singleProjectionService.GetAggregateStateAsync<TEnvironmentAggregatePayload>(aggregateId).Result;
        return aggregate ?? throw new SekibanAggregateNotExistsException(aggregateId, typeof(TEnvironmentAggregatePayload).Name);
    }
    public IReadOnlyCollection<IEvent> GetLatestEvents() => _commandExecutor.LatestEvents;
}
