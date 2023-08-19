using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Documents;
using Sekiban.Core.Events;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Validation;
using Sekiban.Testing.Command;
namespace Sekiban.Testing.SingleProjections;

/// <summary>
///     Test helper for aggregate
/// </summary>
/// <typeparam name="TAggregatePayload"></typeparam>
public interface IAggregateTestHelper<TAggregatePayload> where TAggregatePayload : IAggregatePayloadCommon
{
    #region Subtypes
    /// <summary>
    ///     Check Payload type is expected.
    /// </summary>
    /// <typeparam name="TAggregatePayloadExpected"></typeparam>
    /// <returns>Returns subtype Test Helper, so developer can check payloads or other testing</returns>
    public IAggregateTestHelper<TAggregatePayloadExpected> ThenPayloadTypeShouldBe<TAggregatePayloadExpected>()
        where TAggregatePayloadExpected : IAggregatePayloadCommon;

    /// <summary>
    ///     Get subtype test helper
    /// </summary>
    /// <typeparam name="TAggregateSubtypePayload"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregateSubtypePayload> Subtype<TAggregateSubtypePayload>()
        where TAggregateSubtypePayload : IAggregatePayloadCommon, IApplicableAggregatePayload<TAggregatePayload>;
    #endregion
    #region given and setup
    /// <summary>
    ///     Given a function or other test as a scenario setup
    ///     Since Aggregate Test is fast, it will run in the each scenario. And all executes separately.
    /// </summary>
    /// <param name="initialAction">Given Action</param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> GivenScenario(Action initialAction);
    /// <summary>
    ///     Given Async function or other test as a scenario setup
    /// </summary>
    /// <param name="initialAction"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> GivenScenarioTask(Func<Task> initialAction);
    /// <summary>
    ///     Given a event that already put in the system.
    ///     events should be <see cref="Event{TEventPayload}" /> type
    ///     It could be in the aggregate developer will test or other aggregates.
    /// </summary>
    /// <param name="ev"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEvent(IEvent ev);
    /// <summary>
    ///     Given events that already put in the system.
    ///     events should be <see cref="Event{TEventPayload}" /> type
    ///     It could be in the aggregate developer will test or other aggregates.
    /// </summary>
    /// <param name="events"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEvents(IEnumerable<IEvent> events);
    /// <summary>
    ///     Given events that already put in the system from file.
    ///     events should be <see cref="Event{TEventPayload}" /> type
    ///     It could be in the aggregate developer will test or other aggregates.
    /// </summary>
    /// <param name="filename"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsFile(string filename);
    /// <summary>
    ///     Run a command in environment (but not for the aggregate that will be tested)
    /// </summary>
    /// <param name="command"></param>
    /// <param name="injectingAggregateId"></param>
    /// <typeparam name="TEnvironmentAggregatePayload"></typeparam>
    /// <returns></returns>
    public Guid RunEnvironmentCommand<TEnvironmentAggregatePayload>(ICommand<TEnvironmentAggregatePayload> command, Guid? injectingAggregateId = null)
        where TEnvironmentAggregatePayload : IAggregatePayloadCommon;
    /// <summary>
    ///     Given event that already put in the system and publish to the system.
    ///     events should be <see cref="Event{TEventPayload}" /> type
    ///     It could be in the aggregate developer will test or other aggregates.
    /// </summary>
    /// <param name="ev"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventWithPublish(IEvent ev);
    /// <summary>
    ///     Given event that already put in the system and publish to the system.
    ///     events should be <see cref="Event{TEventPayload}" /> type
    ///     It could be in the aggregate developer will test or other aggregates.
    ///     Even non blocking subscriptions will be executed by same thread and block the execution
    ///     (To test subscription values, use this method. But be careful, orders could be different from actual execution)
    /// </summary>
    /// <param name="ev"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventWithPublishAndBlockingEvent(IEvent ev);
    /// <summary>
    ///     Given event that already put in the system and publish to the system.
    ///     events should be <see cref="Event{TEventPayload}" /> type
    ///     It could be in the aggregate developer will test or other aggregates.
    /// </summary>
    /// <param name="events"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsWithPublish(IEnumerable<IEvent> events);
    /// <summary>
    ///     Given event that already put in the system and publish to the system.
    ///     events should be <see cref="Event{TEventPayload}" /> type
    ///     It could be in the aggregate developer will test or other aggregates.
    ///     Even non blocking subscriptions will be executed by same thread and block the execution
    ///     (To test subscription values, use this method. But be careful, orders could be different from actual execution)
    /// </summary>
    /// <param name="events"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsWithPublishAndBlockingEvents(IEnumerable<IEvent> events);
    /// <summary>
    ///     Given event that already put in the system and publish to the system from file.
    ///     events should be <see cref="Event{TEventPayload}" /> type
    ///     It could be in the aggregate developer will test or other aggregates.
    /// </summary>
    /// <param name="filename"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsFileWithPublish(string filename);
    /// <summary>
    ///     Given event that already put in the system and publish to the system from file.
    ///     events should be <see cref="Event{TEventPayload}" /> type
    ///     It could be in the aggregate developer will test or other aggregates.
    ///     Even non blocking subscriptions will be executed by same thread and block the execution
    ///     (To test subscription values, use this method. But be careful, orders could be different from actual execution)
    /// </summary>
    /// <param name="filename"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsFileWithPublishAndBlockingEvents(string filename);
    /// <summary>
    ///     Run command in environment (but not for the aggregate that will be tested) and publish to the system.
    /// </summary>
    /// <param name="command"></param>
    /// <param name="injectingAggregateId"></param>
    /// <typeparam name="TEnvironmentAggregatePayload"></typeparam>
    /// <returns></returns>
    public Guid RunEnvironmentCommandWithPublish<TEnvironmentAggregatePayload>(
        ICommand<TEnvironmentAggregatePayload> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregatePayload : IAggregatePayloadCommon;
    /// <summary>
    ///     Run command in environment (but not for the aggregate that will be tested) and publish to the system.
    ///     Even non blocking subscriptions will be executed by same thread and block the execution
    ///     (To test subscription values, use this method. But be careful, orders could be different from actual execution)
    /// </summary>
    /// <param name="command"></param>
    /// <param name="injectingAggregateId"></param>
    /// <typeparam name="TEnvironmentAggregatePayload"></typeparam>
    /// <returns></returns>
    public Guid RunEnvironmentCommandWithPublishAndBlockingEvent<TEnvironmentAggregatePayload>(
        ICommand<TEnvironmentAggregatePayload> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregatePayload : IAggregatePayloadCommon;
    /// <summary>
    ///     Run action with command executor
    /// </summary>
    /// <param name="action"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentCommandExecutorAction(Action<TestCommandExecutor> action);
    /// <summary>
    ///     Aggregate Id Holder
    ///     This object keeps aggregate id and root partition key
    ///     Aggregate Test Developers usually don't need to use this object
    /// </summary>
    public IAggregateIdHolder AggregateIdHolder { get; }
    /// <summary>
    ///     Check unhandled errors and if it exists, throw exception
    /// </summary>
    public void ThrowIfTestHasUnhandledErrors();
    #endregion

    #region When
    /// <summary>
    ///     Run a command
    /// </summary>
    /// <param name="command"></param>
    /// <typeparam name="TCommand"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> WhenCommand<TCommand>(TCommand command) where TCommand : ICommand<TAggregatePayload>;
    /// <summary>
    ///     Run a command with Subtype, event will not be published
    /// </summary>
    /// <param name="command"></param>
    /// <typeparam name="TAggregateSubtype"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> WhenSubtypeCommand<TAggregateSubtype>(ICommand<TAggregateSubtype> command)
        where TAggregateSubtype : IAggregateSubtypePayloadParentApplicable<TAggregatePayload>;
    /// <summary>
    ///     Run a command with Subtype, event will be published
    /// </summary>
    /// <param name="command"></param>
    /// <typeparam name="TAggregateSubtype"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> WhenSubtypeCommandWithPublish<TAggregateSubtype>(ICommand<TAggregateSubtype> command)
        where TAggregateSubtype : IAggregateSubtypePayloadParentApplicable<TAggregatePayload>;
    /// <summary>
    ///     Run a command with Subtype, event will be published
    ///     Even non blocking subscriptions will be executed by same thread and block the execution
    ///     (To test subscription values, use this method. But be careful, orders could be different from actual execution)
    /// </summary>
    /// <param name="command"></param>
    /// <typeparam name="TAggregateSubtype"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> WhenSubtypeCommandWithPublishAndBlockingSubscriber<TAggregateSubtype>(
        ICommand<TAggregateSubtype> command) where TAggregateSubtype : IAggregateSubtypePayloadParentApplicable<TAggregatePayload>;

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
