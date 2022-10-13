using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.Queries.SingleAggregates;
using Sekiban.EventSourcing.Shared;
using Sekiban.EventSourcing.TestHelpers.Helpers;
namespace Sekiban.EventSourcing.TestHelpers.ProjectionTests;

public abstract class MultipleProjectionsAndQueriesTestBase<TDependencyDefinition> where TDependencyDefinition : IDependencyDefinition, new()
{
    private readonly AggregateTestCommandExecutor _commandExecutor;
    protected readonly IServiceProvider _serviceProvider;

    // ReSharper disable once PublicConstructorInAbstractClass
    public MultipleProjectionsAndQueriesTestBase()
    {
        var services = new ServiceCollection();
        // ReSharper disable once VirtualMemberCallInConstructor
        SetupDependency(services);
        services.AddQueryFiltersFromDependencyDefinition(new TDependencyDefinition());
        SekibanEventSourcingDependency.RegisterForAggregateTest(services, new TDependencyDefinition());
        _serviceProvider = services.BuildServiceProvider();
        _commandExecutor = new AggregateTestCommandExecutor(_serviceProvider);
    }
    protected abstract void SetupDependency(IServiceCollection serviceCollection);

    public TMultipleProjectionTest SetupMultipleAggregateProjectionTest<TMultipleProjectionTest>()
        where TMultipleProjectionTest : class, ITestHelperEventSubscriber
    {
        var test = Activator.CreateInstance(typeof(TMultipleProjectionTest), _serviceProvider) as TMultipleProjectionTest;
        if (test is null) { throw new InvalidOperationException("Could not create test"); }
        return test;
    }

    public Guid RunCreateCommand<TAggregate>(ICreateAggregateCommand<TAggregate> command, Guid? injectingAggregateId = null)
        where TAggregate : AggregateBase, new()
    {
        var (events, aggregateId) = _commandExecutor.ExecuteCreateCommand(command, injectingAggregateId);
        return aggregateId;
    }
    public void RunChangeCommand<TAggregate>(ChangeAggregateCommandBase<TAggregate> command) where TAggregate : AggregateBase, new()
    {
        var events = _commandExecutor.ExecuteChangeCommand(command);

    }
    public MultipleProjectionsAndQueriesTestBase<TDependencyDefinition> GivenScenario(Action initialAction)
    {
        initialAction();
        return this;
    }

    public AggregateDto<TEnvironmentAggregateContents> GetAggregateDto<TEnvironmentAggregate, TEnvironmentAggregateContents>(Guid aggregateId)
        where TEnvironmentAggregate : TransferableAggregateBase<TEnvironmentAggregateContents>, new()
        where TEnvironmentAggregateContents : IAggregateContents, new()
    {
        var singleAggregateService = _serviceProvider.GetRequiredService(typeof(ISingleAggregateService)) as ISingleAggregateService;
        if (singleAggregateService is null) { throw new Exception("Failed to get single aggregate service"); }
        var aggregate = singleAggregateService.GetAggregateDtoAsync<TEnvironmentAggregate, TEnvironmentAggregateContents>(aggregateId).Result;
        return aggregate ?? throw new SekibanAggregateNotExistsException(aggregateId, typeof(TEnvironmentAggregate).Name);
    }
    public IReadOnlyCollection<IAggregateEvent> GetLatestEvents()
    {
        return _commandExecutor.LatestEvents;
    }
}
