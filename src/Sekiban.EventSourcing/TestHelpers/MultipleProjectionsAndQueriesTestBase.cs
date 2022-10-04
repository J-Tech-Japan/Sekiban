using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.Shared;
namespace Sekiban.EventSourcing.TestHelpers;

public abstract class MultipleProjectionsAndQueriesTestBase
{
    private readonly AggregateTestCommandExecutor _commandExecutor;
    protected readonly IServiceProvider _serviceProvider;

    private readonly List<ITestHelperEventSubscriber> subscribers = new();

    // ReSharper disable once PublicConstructorInAbstractClass
    public MultipleProjectionsAndQueriesTestBase(SekibanDependencyOptions dependencyOptions)
    {
        var services = new ServiceCollection();
        // ReSharper disable once VirtualMemberCallInConstructor
        SetupDependency(services);
        SekibanEventSourcingDependency.RegisterForAggregateTest(services, dependencyOptions);
        _serviceProvider = services.BuildServiceProvider();
        _commandExecutor = new AggregateTestCommandExecutor(_serviceProvider);
    }
    protected abstract void SetupDependency(IServiceCollection serviceCollection);

    public TMultipleProjectionTest SetupMultipleAggregateProjectionTest<TMultipleProjectionTest>()
        where TMultipleProjectionTest : class, ITestHelperEventSubscriber
    {
        var test = Activator.CreateInstance(typeof(TMultipleProjectionTest), _serviceProvider) as TMultipleProjectionTest;
        if (test is null) { throw new InvalidOperationException("Could not create test"); }
        subscribers.Add(test);
        return test;
    }


    public Guid RunCreateCommand<TAggregate>(ICreateAggregateCommand<TAggregate> command) where TAggregate : AggregateBase, new()
    {
        var (events, aggregateId) = _commandExecutor.ExecuteCreateCommand(command);
        var aggregateEvents = events?.ToList() ?? new List<IAggregateEvent>();
        foreach (var subscriber in subscribers)
        {
            foreach (var e in aggregateEvents)
            {
                subscriber.OnEvent(e);
            }
        }
        return aggregateId;
    }
    public void RunChangeCommand<TAggregate>(ChangeAggregateCommandBase<TAggregate> command) where TAggregate : AggregateBase, new()
    {
        var events = _commandExecutor.ExecuteChangeCommand(command);
        var aggregateEvents = events?.ToList() ?? new List<IAggregateEvent>();
        foreach (var subscriber in subscribers)
        {
            foreach (var e in aggregateEvents)
            {
                subscriber.OnEvent(e);
            }
        }
    }
    public MultipleProjectionsAndQueriesTestBase GivenScenario(Action test)
    {
        return this;
    }
}
