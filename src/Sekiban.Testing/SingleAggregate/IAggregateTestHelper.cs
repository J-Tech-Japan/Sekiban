using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Validation;
using Sekiban.Testing.Command;
namespace Sekiban.Testing.SingleAggregate;

public interface IAggregateTestHelper<TAggregatePayload> where TAggregatePayload : IAggregatePayload, new()
{
    #region given and setup
    public TSingleAggregateProjection SetupSingleAggregateProjection<TSingleAggregateProjection>()
        where TSingleAggregateProjection : SingleAggregateTestBase;
    public IAggregateTestHelper<TAggregatePayload> GivenScenario(Action initialAction);

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEvent(IAggregateEvent ev);
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEvents(IEnumerable<IAggregateEvent> events);
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsFile(string filename);
    public AggregateState<TEnvironmentAggregatePayload>
        GetEnvironmentAggregateState<TEnvironmentAggregatePayload>(Guid aggregateId)
        where TEnvironmentAggregatePayload : IAggregatePayload, new();
    public Guid RunEnvironmentCreateCommand<TEnvironmentAggregate>(
        ICreateAggregateCommand<TEnvironmentAggregate> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregate : IAggregatePayload, new();
    public void RunEnvironmentChangeCommand<TEnvironmentAggregate>(ChangeAggregateCommandBase<TEnvironmentAggregate> command)
        where TEnvironmentAggregate : IAggregatePayload, new();
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentCommandExecutorAction(Action<AggregateTestCommandExecutor> action);
    public IReadOnlyCollection<IAggregateEvent> GetLatestEnvironmentEvents();
    #endregion

    #region When
    public IAggregateTestHelper<TAggregatePayload> WhenCreate<C>(C createCommand) where C : ICreateAggregateCommand<TAggregatePayload>;
    public IAggregateTestHelper<TAggregatePayload> WhenChange<C>(C changeCommand) where C : ChangeAggregateCommandBase<TAggregatePayload>;
    public IAggregateTestHelper<TAggregatePayload> WhenChange<C>(Func<AggregateState<TAggregatePayload>, C> commandFunc)
        where C : ChangeAggregateCommandBase<TAggregatePayload>;
    #endregion

    #region Then
    public IAggregateTestHelper<TAggregatePayload> ThenGetEvents(Action<List<IAggregateEvent>> checkEventsAction);
    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleEvent<T>(Action<T> checkEventAction) where T : IAggregateEvent;
    public IAggregateTestHelper<TAggregatePayload> ThenSingleEventIs<T>(AggregateEvent<T> aggregateEvent) where T : IEventPayload;
    public IAggregateTestHelper<TAggregatePayload> ThenSingleEventPayloadIs<T>(T payload) where T : IEventPayload;
    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleEventPayload<T>(Action<T> checkPayloadAction) where T : class, IEventPayload;
    public IAggregateTestHelper<TAggregatePayload> ThenGetState(Action<AggregateState<TAggregatePayload>> checkStateAction);
    public IAggregateTestHelper<TAggregatePayload> ThenStateIs(AggregateState<TAggregatePayload> expectedState);
    public IAggregateTestHelper<TAggregatePayload> ThenGetPayload(Action<TAggregatePayload> payloadAction);
    public IAggregateTestHelper<TAggregatePayload> ThenPayloadIs(TAggregatePayload payload);
    public IAggregateTestHelper<TAggregatePayload> WriteStateToFile(string filename);
    public IAggregateTestHelper<TAggregatePayload> WritePayloadToFile(string filename);
    public IAggregateTestHelper<TAggregatePayload> ThenStateIsFromJson(string stateJson);
    public IAggregateTestHelper<TAggregatePayload> ThenStateIsFromFile(string stateFileName);
    public IAggregateTestHelper<TAggregatePayload> ThenPayloadIsFromJson(string payloadJson);
    public IAggregateTestHelper<TAggregatePayload> ThenPayloadIsFromFile(string payloadFileName);
    public IAggregateTestHelper<TAggregatePayload> ThenThrows<T>() where T : Exception;
    public IAggregateTestHelper<TAggregatePayload> ThenGetException<T>(Action<T> checkException) where T : Exception;
    public IAggregateTestHelper<TAggregatePayload> ThenGetException(Action<Exception> checkException);
    public IAggregateTestHelper<TAggregatePayload> ThenNotThrowsAnException();
    public IAggregateTestHelper<TAggregatePayload> ThenHasValidationErrors(
        IEnumerable<SekibanValidationParameterError> validationParameterErrors);
    public IAggregateTestHelper<TAggregatePayload> ThenHasValidationErrors();
    #endregion

    #region Get
    public Guid GetAggregateId();
    public int GetCurrentVersion();
    public AggregateState<TAggregatePayload> GetAggregateState();
    public Aggregate<TAggregatePayload> GetAggregate();
    #endregion
}
