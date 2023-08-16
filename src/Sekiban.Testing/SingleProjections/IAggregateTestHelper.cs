using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Documents;
using Sekiban.Core.Events;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Validation;
using Sekiban.Testing.Command;
namespace Sekiban.Testing.SingleProjections;

public interface IAggregateTestHelper<TAggregatePayload> where TAggregatePayload : IAggregatePayloadCommonBase
{

    #region Subtypes
    public IAggregateTestHelper<TAggregatePayloadExpected> ThenPayloadTypeShouldBe<TAggregatePayloadExpected>()
        where TAggregatePayloadExpected : IAggregatePayloadCommon;

    public IAggregateTestHelper<TAggregateSubtypePayload> Subtype<TAggregateSubtypePayload>()
        where TAggregateSubtypePayload : IAggregatePayloadCommon, IApplicableAggregatePayload<TAggregatePayload>;
    #endregion
    #region given and setup
    public IAggregateTestHelper<TAggregatePayload> GivenScenario(Action initialAction);
    public IAggregateTestHelper<TAggregatePayload> GivenScenarioTask(Func<Task> initialAction);

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEvent(IEvent ev);
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEvents(IEnumerable<IEvent> events);
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsFile(string filename);

    public Guid RunEnvironmentCommand<TEnvironmentAggregatePayload>(ICommand<TEnvironmentAggregatePayload> command, Guid? injectingAggregateId = null)
        where TEnvironmentAggregatePayload : IAggregatePayloadCommon;

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventWithPublish(IEvent ev);
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventWithPublishAndBlockingEvent(IEvent ev);
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsWithPublish(IEnumerable<IEvent> events);
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsWithPublishAndBlockingEvents(IEnumerable<IEvent> events);
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsFileWithPublish(string filename);
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsFileWithPublishAndBlockingEvents(string filename);

    public Guid RunEnvironmentCommandWithPublish<TEnvironmentAggregatePayload>(
        ICommand<TEnvironmentAggregatePayload> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregatePayload : IAggregatePayloadCommon;

    public Guid RunEnvironmentCommandWithPublishAndBlockingEvent<TEnvironmentAggregatePayload>(
        ICommand<TEnvironmentAggregatePayload> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregatePayload : IAggregatePayloadCommon;

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentCommandExecutorAction(Action<TestCommandExecutor> action);
    public IAggregateIdHolder AggregateIdHolder { get; }

    public void ThrowIfTestHasUnhandledErrors();
    #endregion

    #region When
    public IAggregateTestHelper<TAggregatePayload> WhenCommand<TCommand>(TCommand command) where TCommand : ICommand<TAggregatePayload>;
    public IAggregateTestHelper<TAggregatePayload> WhenSubtypeCommand<TAggregateSubtypePayload, TCommand>(TCommand command)
        where TAggregateSubtypePayload : TAggregatePayload, IAggregatePayloadCommon where TCommand : ICommand<TAggregateSubtypePayload>;

    public IAggregateTestHelper<TAggregatePayload> WhenCommand<TCommand>(Func<AggregateState<TAggregatePayload>, TCommand> commandFunc)
        where TCommand : ICommand<TAggregatePayload>;

    public IAggregateTestHelper<TAggregatePayload> WhenCommandWithPublish<TCommand>(TCommand command) where TCommand : ICommand<TAggregatePayload>;

    public IAggregateTestHelper<TAggregatePayload> WhenCommandWithPublishAndBlockingSubscriber<TCommand>(TCommand command)
        where TCommand : ICommand<TAggregatePayload>;

    public IAggregateTestHelper<TAggregatePayload> WhenCommandWithPublish<TCommand>(Func<AggregateState<TAggregatePayload>, TCommand> commandFunc)
        where TCommand : ICommand<TAggregatePayload>;
    public IAggregateTestHelper<TAggregatePayload> WhenCommandWithPublishAndBlockingSubscriber<TCommand>(
        Func<AggregateState<TAggregatePayload>, TCommand> commandFunc) where TCommand : ICommand<TAggregatePayload>;
    #endregion

    #region Then
    public IAggregateTestHelper<TAggregatePayload> ThenGetLatestEvents(Action<List<IEvent>> checkEventsAction);
    public IAggregateTestHelper<TAggregatePayload> ThenGetAllAggregateEvents(Action<List<IEvent>> checkEventsAction);

    public IAggregateTestHelper<TAggregatePayload> ThenGetLatestSingleEvent<T>(Action<Event<T>> checkEventAction) where T : IEventPayloadCommon;

    public IAggregateTestHelper<TAggregatePayload> ThenLastSingleEventIs<T>(Event<T> @event) where T : IEventPayloadCommon;

    public IAggregateTestHelper<TAggregatePayload> ThenLastSingleEventPayloadIs<T>(T payload) where T : IEventPayloadCommon;

    public IAggregateTestHelper<TAggregatePayload> ThenGetLatestSingleEventPayload<T>(Action<T> checkPayloadAction)
        where T : class, IEventPayloadCommon;

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
    public IAggregateTestHelper<TAggregatePayload> ThenThrowsAnException();

    public IAggregateTestHelper<TAggregatePayload> ThenHasValidationErrors(IEnumerable<SekibanValidationParameterError> validationParameterErrors);

    public IAggregateTestHelper<TAggregatePayload> ThenHasValidationErrors();
    #endregion

    #region Get
    public Guid GetAggregateId();
    public string GetRootPartitionKey();
    public int GetCurrentVersion();
    public AggregateState<TAggregatePayload> GetAggregateState();

    public AggregateState<TEnvironmentAggregatePayload> GetEnvironmentAggregateState<TEnvironmentAggregatePayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey) where TEnvironmentAggregatePayload : IAggregatePayloadCommon;

    public IReadOnlyCollection<IEvent> GetLatestEnvironmentEvents();
    public List<IEvent> GetLatestEvents();
    public List<IEvent> GetAllAggregateEvents(int? toVersion = null);
    #endregion

    #region Single Projection
    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionStateIs<TSingleProjectionPayload>(
        SingleProjectionState<TSingleProjectionPayload> state) where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon;

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionPayloadIs<TSingleProjectionPayload>(TSingleProjectionPayload payload)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon;

    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleProjectionPayload<TSingleProjectionPayload>(
        Action<TSingleProjectionPayload> payloadAction) where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon;

    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleProjectionState<TSingleProjectionPayload>(
        Action<SingleProjectionState<TSingleProjectionPayload>> stateAction) where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon;

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionPayloadIsFromJson<TSingleProjectionPayload>(string payloadJson)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon;

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionPayloadIsFromFile<TSingleProjectionPayload>(string payloadFilename)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon;

    public IAggregateTestHelper<TAggregatePayload> WriteSingleProjectionStateToFile<TSingleProjectionPayload>(string filename)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon;
    #endregion

    #region General List Query Test
    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIs<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        ListQueryResult<TQueryResponse> expectedResponse) where TQueryResponse : IQueryResponse;
    public IAggregateTestHelper<TAggregatePayload> WriteQueryResponseToFile<TQueryResponse>(IListQueryInput<TQueryResponse> param, string filename)
        where TQueryResponse : IQueryResponse;
    public IAggregateTestHelper<TAggregatePayload> ThenGetQueryResponse<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        Action<ListQueryResult<TQueryResponse>> responseAction) where TQueryResponse : IQueryResponse;

    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIsFromJson<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        string responseJson) where TQueryResponse : IQueryResponse;

    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIsFromFile<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        string responseFilename) where TQueryResponse : IQueryResponse;

    public IAggregateTestHelper<TAggregatePayload> ThenQueryThrows<T>(IListQueryInputCommon param) where T : Exception;
    public IAggregateTestHelper<TAggregatePayload> ThenQueryGetException<T>(IListQueryInputCommon param, Action<T> checkException)
        where T : Exception;
    public IAggregateTestHelper<TAggregatePayload> ThenQueryGetException<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        Action<Exception> checkException) where TQueryResponse : IQueryResponse;
    public IAggregateTestHelper<TAggregatePayload> ThenQueryNotThrowsAnException(IListQueryInputCommon param);
    public IAggregateTestHelper<TAggregatePayload> ThenQueryThrowsAnException(IListQueryInputCommon param);
    #endregion

    #region Query Test (not list)
    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIs<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        TQueryResponse expectedResponse) where TQueryResponse : IQueryResponse;
    public IAggregateTestHelper<TAggregatePayload> WriteQueryResponseToFile<TQueryResponse>(IQueryInput<TQueryResponse> param, string filename)
        where TQueryResponse : IQueryResponse;
    public IAggregateTestHelper<TAggregatePayload> ThenGetQueryResponse<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        Action<TQueryResponse> responseAction) where TQueryResponse : IQueryResponse;

    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIsFromJson<TQueryResponse>(IQueryInput<TQueryResponse> param, string responseJson)
        where TQueryResponse : IQueryResponse;

    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIsFromFile<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        string responseFilename) where TQueryResponse : IQueryResponse;

    public IAggregateTestHelper<TAggregatePayload> ThenQueryThrows<T>(IQueryInputCommon param) where T : Exception;
    public IAggregateTestHelper<TAggregatePayload> ThenQueryGetException<T>(IQueryInputCommon param, Action<T> checkException) where T : Exception;
    public IAggregateTestHelper<TAggregatePayload> ThenQueryGetException(IQueryInputCommon param, Action<Exception> checkException);
    public IAggregateTestHelper<TAggregatePayload> ThenQueryNotThrowsAnException(IQueryInputCommon param);

    public IAggregateTestHelper<TAggregatePayload> ThenQueryThrowsAnException(IQueryInputCommon param);
    #endregion
}
