using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Validation;
using Sekiban.Testing.Command;
namespace Sekiban.Testing.SingleAggregate;

public interface IAggregateTestHelper<TAggregate, TContents> where TAggregate : Aggregate<TContents> where TContents : IAggregatePayload, new()
{
    #region given and setup
    public TSingleAggregateProjection SetupSingleAggregateProjection<TSingleAggregateProjection>()
        where TSingleAggregateProjection : SingleAggregateTestBase;
    public IAggregateTestHelper<TAggregate, TContents> GivenScenario(Action initialAction);

    public IAggregateTestHelper<TAggregate, TContents> GivenEnvironmentEvent(IAggregateEvent ev);
    public IAggregateTestHelper<TAggregate, TContents> GivenEnvironmentEvents(IEnumerable<IAggregateEvent> events);
    public IAggregateTestHelper<TAggregate, TContents> GivenEnvironmentEventsFile(string filename);
    public AggregateState<TEnvironmentAggregateContents>
        GetEnvironmentAggregateDto<TEnvironmentAggregate, TEnvironmentAggregateContents>(Guid aggregateId)
        where TEnvironmentAggregate : Aggregate<TEnvironmentAggregateContents>, new()
        where TEnvironmentAggregateContents : IAggregatePayload, new();
    public Guid RunEnvironmentCreateCommand<TEnvironmentAggregate>(
        ICreateAggregateCommand<TEnvironmentAggregate> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregate : AggregateCommonBase, new();
    public void RunEnvironmentChangeCommand<TEnvironmentAggregate>(ChangeAggregateCommandBase<TEnvironmentAggregate> command)
        where TEnvironmentAggregate : AggregateCommonBase, new();
    public IAggregateTestHelper<TAggregate, TContents> GivenEnvironmentCommandExecutorAction(Action<AggregateTestCommandExecutor> action);
    public IReadOnlyCollection<IAggregateEvent> GetLatestEnvironmentEvents();
    #endregion

    #region When
    public IAggregateTestHelper<TAggregate, TContents> WhenCreate<C>(C createCommand) where C : ICreateAggregateCommand<TAggregate>;
    public IAggregateTestHelper<TAggregate, TContents> WhenChange<C>(C changeCommand) where C : ChangeAggregateCommandBase<TAggregate>;
    public IAggregateTestHelper<TAggregate, TContents> WhenChange<C>(Func<TAggregate, C> commandFunc)
        where C : ChangeAggregateCommandBase<TAggregate>;
    
    #endregion

    #region Then
    public IAggregateTestHelper<TAggregate, TContents> ThenGetEvents(Action<List<IAggregateEvent>> checkEventsAction);
    public IAggregateTestHelper<TAggregate, TContents> ThenGetSingleEvent<T>(Action<T> checkEventAction) where T : IAggregateEvent;
    public IAggregateTestHelper<TAggregate, TContents> ThenSingleEventIs<T>(AggregateEvent<T> aggregateEvent) where T : IEventPayload;
    public IAggregateTestHelper<TAggregate, TContents> ThenSingleEventPayloadIs<T>(T payload) where T : IEventPayload;
    public IAggregateTestHelper<TAggregate, TContents> ThenGetSingleEventPayload<T>(Action<T> checkPayloadAction) where T : class, IEventPayload;
    public IAggregateTestHelper<TAggregate, TContents> ThenGetState(Action<AggregateState<TContents>> checkDtoAction);
    public IAggregateTestHelper<TAggregate, TContents> ThenStateIs(AggregateState<TContents> expectedState);
    public IAggregateTestHelper<TAggregate, TContents> ThenGetContents(Action<TContents> contentsAction);
    public IAggregateTestHelper<TAggregate, TContents> ThenContentsIs(TContents contents);
    public IAggregateTestHelper<TAggregate, TContents> WriteStateToFile(string filename);
    public IAggregateTestHelper<TAggregate, TContents> WriteContentsToFile(string filename);
    public IAggregateTestHelper<TAggregate, TContents> ThenStateIsFromJson(string dtoJson);
    public IAggregateTestHelper<TAggregate, TContents> ThenStateIsFromFile(string dtoFileName);
    public IAggregateTestHelper<TAggregate, TContents> ThenContentsIsFromJson(string contentsJson);
    public IAggregateTestHelper<TAggregate, TContents> ThenContentsIsFromFile(string contentsFileName);
    public IAggregateTestHelper<TAggregate, TContents> ThenThrows<T>() where T : Exception;
    public IAggregateTestHelper<TAggregate, TContents> ThenGetException<T>(Action<T> checkException) where T : Exception;
    public IAggregateTestHelper<TAggregate, TContents> ThenGetException(Action<Exception> checkException);
    public IAggregateTestHelper<TAggregate, TContents> ThenNotThrowsAnException();
    public IAggregateTestHelper<TAggregate, TContents> ThenHasValidationErrors(
        IEnumerable<SekibanValidationParameterError> validationParameterErrors);
    public IAggregateTestHelper<TAggregate, TContents> ThenHasValidationErrors();
    #endregion

    #region Get
    public Guid GetAggregateId();
    public int GetCurrentVersion();
    public TAggregate GetAggregate();
    #endregion

}
