using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Dependency;
using Sekiban.Core.Event;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Validation;
using Sekiban.Testing.Command;
namespace Sekiban.Testing.SingleProjections;

public abstract class AggregateTestBase<TAggregatePayload, TDependencyDefinition> : IDisposable, IAggregateTestHelper<TAggregatePayload>
    where TAggregatePayload : IAggregatePayload, new()
    where TDependencyDefinition : IDependencyDefinition, new()
{
    private readonly IAggregateTestHelper<TAggregatePayload> _helper;
    protected readonly IServiceProvider _serviceProvider;
    protected AggregateTestBase()
    {
        var services = new ServiceCollection();
        // ReSharper disable once VirtualMemberCallInConstructor
        SetupDependency(services);
        services.AddQueriesFromDependencyDefinition(new TDependencyDefinition());
        services.AddSekibanCoreForAggregateTestWithDependency(new TDependencyDefinition());
        _serviceProvider = services.BuildServiceProvider();
        _helper = new AggregateTestHelper<TAggregatePayload>(_serviceProvider);
    }
    public IAggregateTestHelper<TAggregatePayload> GivenScenario(Action initialAction) => _helper.GivenScenario(initialAction);
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEvent(IEvent ev) => _helper.GivenEnvironmentEvent(ev);
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEvents(IEnumerable<IEvent> events) => _helper.GivenEnvironmentEvents(events);
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsFile(string filename) => _helper.GivenEnvironmentEventsFile(filename);
    public AggregateIdentifierState<TEnvironmentAggregatePayload>
        GetEnvironmentAggregateState<TEnvironmentAggregatePayload>(Guid aggregateId)
        where TEnvironmentAggregatePayload : IAggregatePayload, new() =>
        _helper.GetEnvironmentAggregateState<TEnvironmentAggregatePayload>(aggregateId);
    public Guid RunEnvironmentCreateCommand<TEnvironmentAggregatePayload>(
        ICreateCommand<TEnvironmentAggregatePayload> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregatePayload : IAggregatePayload, new() =>
        _helper.RunEnvironmentCreateCommand(command, injectingAggregateId);
    public void RunEnvironmentChangeCommand<TEnvironmentAggregatePayload>(ChangeCommandBase<TEnvironmentAggregatePayload> command)
        where TEnvironmentAggregatePayload : IAggregatePayload, new()
    {
        _helper.RunEnvironmentChangeCommand(command);
    }
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentCommandExecutorAction(Action<TestCommandExecutor> action)
    {
        _helper.GivenEnvironmentCommandExecutorAction(action);
        return this;
    }
    public IReadOnlyCollection<IEvent> GetLatestEnvironmentEvents() => _helper.GetLatestEnvironmentEvents();
    public IAggregateTestHelper<TAggregatePayload> WhenCreate<C>(C createCommand) where C : ICreateCommand<TAggregatePayload> =>
        _helper.WhenCreate(createCommand);
    public IAggregateTestHelper<TAggregatePayload> WhenChange<C>(C changeCommand) where C : ChangeCommandBase<TAggregatePayload> =>
        _helper.WhenChange(changeCommand);
    public IAggregateTestHelper<TAggregatePayload> WhenChange<C>(Func<AggregateIdentifierState<TAggregatePayload>, C> commandFunc)
        where C : ChangeCommandBase<TAggregatePayload> => _helper.WhenChange(commandFunc);

    public IAggregateTestHelper<TAggregatePayload> ThenGetEvents(Action<List<IEvent>> checkEventsAction) => _helper.ThenGetEvents(checkEventsAction);
    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleEvent<T>(Action<T> checkEventAction) where T : IEvent =>
        _helper.ThenGetSingleEvent(checkEventAction);
    public IAggregateTestHelper<TAggregatePayload> ThenSingleEventIs<T>(Event<T> @event) where T : IEventPayload => _helper.ThenSingleEventIs(@event);
    public IAggregateTestHelper<TAggregatePayload> ThenSingleEventPayloadIs<T>(T payload) where T : IEventPayload =>
        _helper.ThenSingleEventPayloadIs(payload);
    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleEventPayload<T>(Action<T> checkPayloadAction) where T : class, IEventPayload =>
        _helper.ThenGetSingleEventPayload(checkPayloadAction);
    public IAggregateTestHelper<TAggregatePayload> ThenGetState(Action<AggregateIdentifierState<TAggregatePayload>> checkStateAction) =>
        _helper.ThenGetState(checkStateAction);
    public IAggregateTestHelper<TAggregatePayload> ThenStateIs(AggregateIdentifierState<TAggregatePayload> expectedIdentifierState) =>
        _helper.ThenStateIs(expectedIdentifierState);
    public IAggregateTestHelper<TAggregatePayload> ThenGetPayload(Action<TAggregatePayload> payloadAction) => _helper.ThenGetPayload(payloadAction);
    public IAggregateTestHelper<TAggregatePayload> ThenPayloadIs(TAggregatePayload payload) => _helper.ThenPayloadIs(payload);
    public IAggregateTestHelper<TAggregatePayload> WriteStateToFile(string filename) => _helper.WriteStateToFile(filename);
    public IAggregateTestHelper<TAggregatePayload> WritePayloadToFile(string filename) => _helper.WriteStateToFile(filename);
    public IAggregateTestHelper<TAggregatePayload> ThenStateIsFromJson(string stateJson) => _helper.ThenStateIsFromJson(stateJson);
    public IAggregateTestHelper<TAggregatePayload> ThenStateIsFromFile(string stateFileName) => _helper.ThenStateIsFromFile(stateFileName);
    public IAggregateTestHelper<TAggregatePayload> ThenPayloadIsFromJson(string payloadJson) => _helper.ThenPayloadIsFromJson(payloadJson);
    public IAggregateTestHelper<TAggregatePayload> ThenPayloadIsFromFile(string payloadFileName) => _helper.ThenPayloadIsFromFile(payloadFileName);
    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleProjectionTest<TSingleProjection, TSingleProjectionPayload>(
        Action<ProjectionTest<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>> singleProjectionTestAction)
        where TSingleProjection : ProjectionBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>, new()
        where TSingleProjectionPayload : ISingleProjectionPayload => _helper.ThenGetSingleProjectionTest(singleProjectionTestAction);
    public Guid GetAggregateId() => _helper.GetAggregateId();
    public int GetCurrentVersion() => _helper.GetCurrentVersion();
    public AggregateIdentifierState<TAggregatePayload> GetAggregateState() => _helper.GetAggregateState();
    public AggregateIdentifier<TAggregatePayload> GetAggregate() => _helper.GetAggregate();

    public IAggregateTestHelper<TAggregatePayload> ThenThrows<T>() where T : Exception => _helper.ThenThrows<T>();
    public IAggregateTestHelper<TAggregatePayload> ThenGetException<T>(Action<T> checkException) where T : Exception =>
        _helper.ThenGetException(checkException);
    public IAggregateTestHelper<TAggregatePayload> ThenGetException(Action<Exception> checkException) => _helper.ThenGetException(checkException);
    public IAggregateTestHelper<TAggregatePayload> ThenNotThrowsAnException() => _helper.ThenNotThrowsAnException();
    public IAggregateTestHelper<TAggregatePayload> ThenThrowsAnException() => _helper.ThenThrowsAnException();
    public IAggregateTestHelper<TAggregatePayload> ThenHasValidationErrors(IEnumerable<SekibanValidationParameterError> validationParameterErrors) =>
        _helper.ThenHasValidationErrors(validationParameterErrors);
    public IAggregateTestHelper<TAggregatePayload> ThenHasValidationErrors() => _helper.ThenHasValidationErrors();

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