using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Validation;
using Sekiban.Testing.Command;
namespace Sekiban.Testing.SingleProjections;

public interface IAggregateTestHelper<TAggregatePayload> where TAggregatePayload : IAggregatePayload, new()
{
    #region given and setup
    public IAggregateTestHelper<TAggregatePayload> GivenScenario(Action initialAction);

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEvent(IEvent ev);
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEvents(IEnumerable<IEvent> events);
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsFile(string filename);

    public Guid RunEnvironmentCommand<TEnvironmentAggregatePayload>(
        ICommand<TEnvironmentAggregatePayload> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregatePayload : IAggregatePayload, new();

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventWithPublish(IEvent ev);
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsWithPublish(IEnumerable<IEvent> events);
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsFileWithPublish(string filename);

    public Guid RunEnvironmentCommandWithPublish<TEnvironmentAggregatePayload>(
        ICommand<TEnvironmentAggregatePayload> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregatePayload : IAggregatePayload, new();

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentCommandExecutorAction(
        Action<TestCommandExecutor> action);
    #endregion

    #region When
    public IAggregateTestHelper<TAggregatePayload> WhenCommand<C>(C changeCommand)
        where C : ICommand<TAggregatePayload>;

    public IAggregateTestHelper<TAggregatePayload> WhenCommand<C>(
        Func<AggregateState<TAggregatePayload>, C> commandFunc)
        where C : ICommand<TAggregatePayload>;

    public IAggregateTestHelper<TAggregatePayload> WhenCommandWithPublish<C>(C changeCommand)
        where C : ICommand<TAggregatePayload>;

    public IAggregateTestHelper<TAggregatePayload> WhenCommandWithPublish<C>(
        Func<AggregateState<TAggregatePayload>, C> commandFunc)
        where C : ICommand<TAggregatePayload>;
    #endregion

    #region Then
    public IAggregateTestHelper<TAggregatePayload> ThenGetLatestEvents(Action<List<IEvent>> checkEventsAction);
    public IAggregateTestHelper<TAggregatePayload> ThenGetAllAggregateEvents(Action<List<IEvent>> checkEventsAction);

    public IAggregateTestHelper<TAggregatePayload> ThenGetLatestSingleEvent<T>(Action<Event<T>> checkEventAction)
        where T : IEventPayloadCommon;

    public IAggregateTestHelper<TAggregatePayload> ThenLastSingleEventIs<T>(Event<T> @event)
        where T : IEventPayloadCommon;

    public IAggregateTestHelper<TAggregatePayload> ThenLastSingleEventPayloadIs<T>(T payload)
        where T : IEventPayloadCommon;

    public IAggregateTestHelper<TAggregatePayload> ThenGetLatestSingleEventPayload<T>(Action<T> checkPayloadAction)
        where T : class, IEventPayloadCommon;

    public IAggregateTestHelper<TAggregatePayload> ThenGetState(
        Action<AggregateState<TAggregatePayload>> checkStateAction);

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

    public IAggregateTestHelper<TAggregatePayload> ThenHasValidationErrors(
        IEnumerable<SekibanValidationParameterError> validationParameterErrors);

    public IAggregateTestHelper<TAggregatePayload> ThenHasValidationErrors();
    #endregion

    #region Get
    public Guid GetAggregateId();
    public int GetCurrentVersion();
    public AggregateState<TAggregatePayload> GetAggregateState();
    public Aggregate<TAggregatePayload> GetAggregate();

    public AggregateState<TEnvironmentAggregatePayload>
        GetEnvironmentAggregateState<TEnvironmentAggregatePayload>(Guid aggregateId)
        where TEnvironmentAggregatePayload : IAggregatePayload, new();

    public IReadOnlyCollection<IEvent> GetLatestEnvironmentEvents();
    public List<IEvent> GetLatestEvents();
    public List<IEvent> GetAllAggregateEvents(int? toVersion = null);
    #endregion

    #region Single Projection
    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionStateIs<TSingleProjectionPayload>(
        SingleProjectionState<TSingleProjectionPayload> state)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new();

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionPayloadIs<TSingleProjectionPayload>(
        TSingleProjectionPayload payload)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new();

    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleProjectionPayload<TSingleProjectionPayload>(
        Action<TSingleProjectionPayload> payloadAction)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new();

    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleProjectionState<TSingleProjectionPayload>(
        Action<SingleProjectionState<TSingleProjectionPayload>> stateAction)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new();

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionPayloadIsFromJson<TSingleProjectionPayload>(
        string payloadJson)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new();

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionPayloadIsFromFile<TSingleProjectionPayload>(
        string payloadFilename)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new();

    public IAggregateTestHelper<TAggregatePayload> WriteSingleProjectionStateToFile<TSingleProjectionPayload>(
        string filename)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new();
    #endregion

    #region General List Query Test
    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIs<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        ListQueryResult<TQueryResponse> expectedResponse)
        where TQueryResponse : IQueryResponse;
    public IAggregateTestHelper<TAggregatePayload> WriteQueryResponseToFile<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        string filename)
        where TQueryResponse : IQueryResponse;
    public IAggregateTestHelper<TAggregatePayload> ThenGetQueryResponse<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        Action<ListQueryResult<TQueryResponse>> responseAction)
        where TQueryResponse : IQueryResponse;

    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIsFromJson<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        string responseJson)
        where TQueryResponse : IQueryResponse;

    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIsFromFile<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        string responseFilename)
        where TQueryResponse : IQueryResponse;
    #endregion

    #region Query Test (not list)
    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIs<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        TQueryResponse expectedResponse)
        where TQueryResponse : IQueryResponse;
    public IAggregateTestHelper<TAggregatePayload> WriteQueryResponseToFile<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        string filename)
        where TQueryResponse : IQueryResponse;
    public IAggregateTestHelper<TAggregatePayload> ThenGetQueryResponse<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        Action<TQueryResponse> responseAction)
        where TQueryResponse : IQueryResponse;

    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIsFromJson<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        string responseJson)
        where TQueryResponse : IQueryResponse;

    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIsFromFile<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        string responseFilename)
        where TQueryResponse : IQueryResponse;
    #endregion
}
