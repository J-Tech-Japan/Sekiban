using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Dependency;
using Sekiban.Core.Event;
using Sekiban.Core.Validation;
using Sekiban.Testing.Command;
namespace Sekiban.Testing.SingleAggregate;

public abstract class SingleAggregateTestBase<TAggregatePayload, TDependencyDefinition> : IDisposable, IAggregateTestHelper<TAggregatePayload>
    where TAggregatePayload : IAggregatePayload, new()
    where TDependencyDefinition : IDependencyDefinition, new()
{
    private readonly IAggregateTestHelper<TAggregatePayload> _helper;
    protected readonly IServiceProvider _serviceProvider;
    protected SingleAggregateTestBase()
    {
        var services = new ServiceCollection();
        // ReSharper disable once VirtualMemberCallInConstructor
        SetupDependency(services);
        services.AddQueryFiltersFromDependencyDefinition(new TDependencyDefinition());
        services.AddSekibanCoreForAggregateTestWithDependency(new TDependencyDefinition());
        _serviceProvider = services.BuildServiceProvider();
        _helper = new AggregateTestHelper<TAggregatePayload>(_serviceProvider);
    }
    public TSingleAggregateProjection SetupSingleAggregateProjection<TSingleAggregateProjection>()
        where TSingleAggregateProjection : SingleAggregateTestBase
    {
        return _helper.SetupSingleAggregateProjection<TSingleAggregateProjection>();
    }
    public IAggregateTestHelper<TAggregatePayload> GivenScenario(Action initialAction)
    {
        return _helper.GivenScenario(initialAction);
    }
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEvent(IAggregateEvent ev)
    {
        return _helper.GivenEnvironmentEvent(ev);
    }
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEvents(IEnumerable<IAggregateEvent> events)
    {
        return _helper.GivenEnvironmentEvents(events);
    }
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsFile(string filename)
    {
        return _helper.GivenEnvironmentEventsFile(filename);
    }
    public AggregateState<TEnvironmentAggregatePayload>
        GetEnvironmentAggregateState<TEnvironmentAggregatePayload>(Guid aggregateId)
        where TEnvironmentAggregatePayload : IAggregatePayload, new()
    {
        return _helper.GetEnvironmentAggregateState<TEnvironmentAggregatePayload>(aggregateId);
    }
    public Guid RunEnvironmentCreateCommand<TEnvironmentAggregate>(
        ICreateAggregateCommand<TEnvironmentAggregate> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregate : IAggregatePayload, new()
    {
        return _helper.RunEnvironmentCreateCommand(command, injectingAggregateId);
    }
    public void RunEnvironmentChangeCommand<TEnvironmentAggregate>(ChangeAggregateCommandBase<TEnvironmentAggregate> command)
        where TEnvironmentAggregate : IAggregatePayload, new()
    {
        _helper.RunEnvironmentChangeCommand(command);
    }
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentCommandExecutorAction(Action<AggregateTestCommandExecutor> action)
    {
        _helper.GivenEnvironmentCommandExecutorAction(action);
        return this;
    }
    public IReadOnlyCollection<IAggregateEvent> GetLatestEnvironmentEvents()
    {
        return _helper.GetLatestEnvironmentEvents();
    }
    public IAggregateTestHelper<TAggregatePayload> WhenCreate<C>(C createCommand) where C : ICreateAggregateCommand<TAggregatePayload>
    {
        return _helper.WhenCreate(createCommand);
    }
    public IAggregateTestHelper<TAggregatePayload> WhenChange<C>(C changeCommand) where C : ChangeAggregateCommandBase<TAggregatePayload>
    {
        return _helper.WhenChange(changeCommand);
    }
    public IAggregateTestHelper<TAggregatePayload> WhenChange<C>(Func<Aggregate<TAggregatePayload>, C> commandFunc)
        where C : ChangeAggregateCommandBase<TAggregatePayload>
    {
        return _helper.WhenChange(commandFunc);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenGetEvents(Action<List<IAggregateEvent>> checkEventsAction)
    {
        return _helper.ThenGetEvents(checkEventsAction);
    }
    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleEvent<T>(Action<T> checkEventAction) where T : IAggregateEvent
    {
        return _helper.ThenGetSingleEvent(checkEventAction);
    }
    public IAggregateTestHelper<TAggregatePayload> ThenSingleEventIs<T>(AggregateEvent<T> aggregateEvent) where T : IEventPayload
    {
        return _helper.ThenSingleEventIs(aggregateEvent);
    }
    public IAggregateTestHelper<TAggregatePayload> ThenSingleEventPayloadIs<T>(T payload) where T : IEventPayload
    {
        return _helper.ThenSingleEventPayloadIs(payload);
    }
    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleEventPayload<T>(Action<T> checkPayloadAction) where T : class, IEventPayload
    {
        return _helper.ThenGetSingleEventPayload(checkPayloadAction);
    }
    public IAggregateTestHelper<TAggregatePayload> ThenGetState(Action<AggregateState<TAggregatePayload>> checkStateAction)
    {
        return _helper.ThenGetState(checkStateAction);
    }
    public IAggregateTestHelper<TAggregatePayload> ThenStateIs(AggregateState<TAggregatePayload> expectedState)
    {
        return _helper.ThenStateIs(expectedState);
    }
    public IAggregateTestHelper<TAggregatePayload> ThenGetPayload(Action<TAggregatePayload> payloadAction)
    {
        return _helper.ThenGetPayload(payloadAction);
    }
    public IAggregateTestHelper<TAggregatePayload> ThenPayloadIs(TAggregatePayload payload)
    {
        return _helper.ThenPayloadIs(payload);
    }
    public IAggregateTestHelper<TAggregatePayload> WriteStateToFile(string filename)
    {
        return _helper.WriteStateToFile(filename);
    }
    public IAggregateTestHelper<TAggregatePayload> WritePayloadToFile(string filename)
    {
        return _helper.WriteStateToFile(filename);
    }
    public IAggregateTestHelper<TAggregatePayload> ThenStateIsFromJson(string stateJson)
    {
        return _helper.ThenStateIsFromJson(stateJson);
    }
    public IAggregateTestHelper<TAggregatePayload> ThenStateIsFromFile(string stateFileName)
    {
        return _helper.ThenStateIsFromFile(stateFileName);
    }
    public IAggregateTestHelper<TAggregatePayload> ThenPayloadIsFromJson(string payloadJson)
    {
        return _helper.ThenPayloadIsFromJson(payloadJson);
    }
    public IAggregateTestHelper<TAggregatePayload> ThenPayloadIsFromFile(string payloadFileName)
    {
        return _helper.ThenPayloadIsFromFile(payloadFileName);
    }
    public Guid GetAggregateId()
    {
        return _helper.GetAggregateId();
    }
    public int GetCurrentVersion()
    {
        return _helper.GetCurrentVersion();
    }
    public Aggregate<TAggregatePayload> GetAggregate()
    {
        return _helper.GetAggregate();
    }

    public IAggregateTestHelper<TAggregatePayload> ThenThrows<T>() where T : Exception
    {
        return _helper.ThenThrows<T>();
    }
    public IAggregateTestHelper<TAggregatePayload> ThenGetException<T>(Action<T> checkException) where T : Exception
    {
        return _helper.ThenGetException(checkException);
    }
    public IAggregateTestHelper<TAggregatePayload> ThenGetException(Action<Exception> checkException)
    {
        return _helper.ThenGetException(checkException);
    }
    public IAggregateTestHelper<TAggregatePayload> ThenNotThrowsAnException()
    {
        return _helper.ThenNotThrowsAnException();
    }
    public IAggregateTestHelper<TAggregatePayload> ThenHasValidationErrors(IEnumerable<SekibanValidationParameterError> validationParameterErrors)
    {
        return _helper.ThenHasValidationErrors(validationParameterErrors);
    }
    public IAggregateTestHelper<TAggregatePayload> ThenHasValidationErrors()
    {
        return _helper.ThenHasValidationErrors();
    }

    public void Dispose() { }

    protected virtual void SetupDependency(IServiceCollection serviceCollection)
    {

    }
    public T GetService<T>()
    {
        var toreturn = _serviceProvider.GetService<T>();
        if (toreturn is null)
        {
            throw new Exception("オブジェクトが登録されていません。" + typeof(T));
        }
        return toreturn;
    }
}
