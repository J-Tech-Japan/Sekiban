using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Dependency;
using Sekiban.Core.Documents;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Validation;
using Sekiban.Testing.Command;
using Xunit.Abstractions;
using Xunit.Sdk;
namespace Sekiban.Testing.SingleProjections;

/// <summary>
/// </summary>
/// <typeparam name="TAggregatePayload">Test Target AggregatePayload</typeparam>
/// <typeparam name="TDependencyDefinition">Dependency Definition</typeparam>
public class
    AggregateTest<TAggregatePayload, TDependencyDefinition> : IDisposable, IAggregateTestHelper<TAggregatePayload>
    where TAggregatePayload : IAggregatePayloadCommon where TDependencyDefinition : IDependencyDefinition, new()
{
    private readonly IAggregateTestHelper<TAggregatePayload> _helper;
    protected readonly IServiceProvider _serviceProvider;
    public AggregateTest()
    {
        var services = new ServiceCollection();
        // ReSharper disable once VirtualMemberCallInConstructor
        SetupDependency(services);
        services.AddQueriesFromDependencyDefinition(new TDependencyDefinition());
        services.AddSekibanCoreForAggregateTestWithDependency(new TDependencyDefinition());
        var outputHelper = new TestOutputHelper();
        services.AddSingleton<ITestOutputHelper>(outputHelper);
        services.AddLogging(builder => builder.AddXUnit(outputHelper));
        _serviceProvider = services.BuildServiceProvider();
        _helper = new AggregateTestHelper<TAggregatePayload>(_serviceProvider);
    }

    public AggregateTest(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new InvalidOperationException();
        _helper = new AggregateTestHelper<TAggregatePayload>(_serviceProvider);
    }

    public AggregateTest(IServiceProvider serviceProvider, Guid aggregateId, string rootPartitionKey) : this(
        serviceProvider)
    {
        _helper = new AggregateTestHelper<TAggregatePayload>(_serviceProvider);
        _helper.AggregateIdHolder.AggregateId = aggregateId;
        _helper.AggregateIdHolder.RootPartitionKey = rootPartitionKey;
    }

    public IAggregateTestHelper<TAggregatePayloadExpected> ThenPayloadTypeShouldBe<TAggregatePayloadExpected>()
        where TAggregatePayloadExpected : IAggregatePayloadCommon =>
        _helper.ThenPayloadTypeShouldBe<TAggregatePayloadExpected>();
    public IAggregateTestHelper<TAggregateSubtypePayload> Subtype<TAggregateSubtypePayload>()
        where TAggregateSubtypePayload : IAggregatePayloadCommon, IApplicableAggregatePayload<TAggregatePayload> =>
        _helper.Subtype<TAggregateSubtypePayload>();

    public IAggregateTestHelper<TAggregatePayload> GivenScenario(Action initialAction) =>
        _helper.GivenScenario(initialAction);
    public IAggregateTestHelper<TAggregatePayload> GivenScenarioTask(Func<Task> initialAction) =>
        _helper.GivenScenarioTask(initialAction);

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEvent(IEvent ev) =>
        _helper.GivenEnvironmentEvent(ev);

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEvents(IEnumerable<IEvent> events) =>
        _helper.GivenEnvironmentEvents(events);

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsFile(string filename) =>
        _helper.GivenEnvironmentEventsFile(filename);

    public AggregateState<TEnvironmentAggregatePayload> GetEnvironmentAggregateState<TEnvironmentAggregatePayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey)
        where TEnvironmentAggregatePayload : IAggregatePayloadCommon =>
        _helper.GetEnvironmentAggregateState<TEnvironmentAggregatePayload>(aggregateId, rootPartitionKey);

    public Guid RunEnvironmentCommand<TEnvironmentAggregatePayload>(
        ICommand<TEnvironmentAggregatePayload> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregatePayload : IAggregatePayloadCommon =>
        _helper.RunEnvironmentCommand(command, injectingAggregateId);
    public Guid GivenEnvironmentCommand<TEnvironmentAggregatePayload>(
        ICommand<TEnvironmentAggregatePayload> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregatePayload : IAggregatePayloadCommon =>
        RunEnvironmentCommand(command, injectingAggregateId);

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventWithPublish(IEvent ev) =>
        _helper.GivenEnvironmentEventWithPublish(ev);
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventWithPublishAndBlockingEvent(IEvent ev) =>
        _helper.GivenEnvironmentEventWithPublish(ev);

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsWithPublish(IEnumerable<IEvent> events) =>
        _helper.GivenEnvironmentEventsWithPublish(events);
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsWithPublishAndBlockingEvents(
        IEnumerable<IEvent> events) =>
        _helper.GivenEnvironmentEventsWithPublish(events);

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsFileWithPublish(string filename) =>
        _helper.GivenEnvironmentEventsFileWithPublish(filename);
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsFileWithPublishAndBlockingEvents(
        string filename) =>
        _helper.GivenEnvironmentEventsFileWithPublish(filename);

    public Guid RunEnvironmentCommandWithPublish<TEnvironmentAggregatePayload>(
        ICommand<TEnvironmentAggregatePayload> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregatePayload : IAggregatePayloadCommon =>
        _helper.RunEnvironmentCommandWithPublish(command, injectingAggregateId);
    public Guid GivenEnvironmentCommandWithPublish<TEnvironmentAggregatePayload>(
        ICommand<TEnvironmentAggregatePayload> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregatePayload : IAggregatePayloadCommon =>
        RunEnvironmentCommandWithPublish(command, injectingAggregateId);
    public Guid RunEnvironmentCommandWithPublishAndBlockingEvent<TEnvironmentAggregatePayload>(
        ICommand<TEnvironmentAggregatePayload> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregatePayload : IAggregatePayloadCommon =>
        _helper.RunEnvironmentCommandWithPublishAndBlockingEvent(command, injectingAggregateId);
    public Guid GivenEnvironmentCommandWithPublishAndBlockingEvent<TEnvironmentAggregatePayload>(
        ICommand<TEnvironmentAggregatePayload> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregatePayload : IAggregatePayloadCommon =>
        RunEnvironmentCommandWithPublishAndBlockingEvent(command, injectingAggregateId);

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentCommandExecutorAction(
        Action<TestCommandExecutor> action)
    {
        _helper.GivenEnvironmentCommandExecutorAction(action);
        return this;
    }
    public IAggregateIdHolder AggregateIdHolder => _helper.AggregateIdHolder;
    public void ThrowIfTestHasUnhandledErrors()
    {
        _helper.ThrowIfTestHasUnhandledErrors();
    }
    public IAggregateTestHelper<TAggregatePayload> GivenCommand<TCommand>(TCommand command)
        where TCommand : ICommand<TAggregatePayload> =>
        WhenCommand(command);
    public IAggregateTestHelper<TAggregatePayload> GivenSubtypeCommand<TAggregateSubtype>(
        ICommand<TAggregateSubtype> command)
        where TAggregateSubtype : IAggregateSubtypePayloadParentApplicable<TAggregatePayload> =>
        WhenSubtypeCommand(command);
    public IAggregateTestHelper<TAggregatePayload> GivenSubtypeCommandWithPublish<TAggregateSubtype>(
        ICommand<TAggregateSubtype> command)
        where TAggregateSubtype : IAggregateSubtypePayloadParentApplicable<TAggregatePayload> =>
        WhenSubtypeCommandWithPublish(command);
    public IAggregateTestHelper<TAggregatePayload>
        GivenSubtypeCommandWithPublishAndBlockingSubscriber<TAggregateSubtype>(ICommand<TAggregateSubtype> command)
        where TAggregateSubtype : IAggregateSubtypePayloadParentApplicable<TAggregatePayload> =>
        WhenSubtypeCommandWithPublishAndBlockingSubscriber(command);
    public IAggregateTestHelper<TAggregatePayload> GivenCommand<TCommand>(
        Func<AggregateState<TAggregatePayload>, TCommand> commandFunc) where TCommand : ICommand<TAggregatePayload> =>
        WhenCommand(commandFunc);
    public IAggregateTestHelper<TAggregatePayload> GivenCommandWithPublish<TCommand>(TCommand command)
        where TCommand : ICommand<TAggregatePayload> =>
        WhenCommandWithPublish(command);
    public IAggregateTestHelper<TAggregatePayload> GivenCommandWithPublishAndBlockingSubscriber<TCommand>(
        TCommand command) where TCommand : ICommand<TAggregatePayload> =>
        WhenCommandWithPublishAndBlockingSubscriber(command);
    public IAggregateTestHelper<TAggregatePayload> GivenCommandWithPublish<TCommand>(
        Func<AggregateState<TAggregatePayload>, TCommand> commandFunc) where TCommand : ICommand<TAggregatePayload> =>
        WhenCommandWithPublish(commandFunc);
    public IAggregateTestHelper<TAggregatePayload> GivenCommandWithPublishAndBlockingSubscriber<TCommand>(
        Func<AggregateState<TAggregatePayload>, TCommand> commandFunc) where TCommand : ICommand<TAggregatePayload> =>
        WhenCommandWithPublishAndBlockingSubscriber(commandFunc);

    public IReadOnlyCollection<IEvent> GetLatestEnvironmentEvents() => _helper.GetLatestEnvironmentEvents();

    public List<IEvent> GetLatestEvents() => _helper.GetLatestEvents();

    public List<IEvent> GetAllAggregateEvents(int? toVersion = null) => _helper.GetAllAggregateEvents(toVersion);

    public IAggregateTestHelper<TAggregatePayload> WhenCommand<TCommand>(TCommand command)
        where TCommand : ICommand<TAggregatePayload> =>
        _helper.WhenCommand(command);

    public IAggregateTestHelper<TAggregatePayload>
        WhenSubtypeCommandWithPublishAndBlockingSubscriber<TAggregateSubtype>(ICommand<TAggregateSubtype> command)
        where TAggregateSubtype : IAggregateSubtypePayloadParentApplicable<TAggregatePayload> =>
        _helper.WhenSubtypeCommandWithPublishAndBlockingSubscriber(command);
    public IAggregateTestHelper<TAggregatePayload> WhenCommand<TCommand>(
        Func<AggregateState<TAggregatePayload>, TCommand> commandFunc) where TCommand : ICommand<TAggregatePayload> =>
        _helper.WhenCommand(commandFunc);

    public IAggregateTestHelper<TAggregatePayload> WhenCommandWithPublish<TCommand>(TCommand changeCommand)
        where TCommand : ICommand<TAggregatePayload> =>
        _helper.WhenCommandWithPublish(changeCommand);
    public IAggregateTestHelper<TAggregatePayload> WhenCommandWithPublishAndBlockingSubscriber<TCommand>(
        TCommand command) where TCommand : ICommand<TAggregatePayload> =>
        _helper.WhenCommandWithPublishAndBlockingSubscriber(command);

    public IAggregateTestHelper<TAggregatePayload> WhenCommandWithPublish<TCommand>(
        Func<AggregateState<TAggregatePayload>, TCommand> commandFunc) where TCommand : ICommand<TAggregatePayload> =>
        _helper.WhenCommandWithPublish(commandFunc);
    public IAggregateTestHelper<TAggregatePayload> WhenCommandWithPublishAndBlockingSubscriber<TCommand>(
        Func<AggregateState<TAggregatePayload>, TCommand> commandFunc) where TCommand : ICommand<TAggregatePayload> =>
        _helper.WhenCommandWithPublishAndBlockingSubscriber(commandFunc);

    public IAggregateTestHelper<TAggregatePayload> ThenGetLatestEvents(Action<List<IEvent>> checkEventsAction) =>
        _helper.ThenGetLatestEvents(checkEventsAction);

    public IAggregateTestHelper<TAggregatePayload> ThenGetAllAggregateEvents(Action<List<IEvent>> checkEventsAction) =>
        _helper.ThenGetAllAggregateEvents(checkEventsAction);

    public IAggregateTestHelper<TAggregatePayload> ThenGetLatestSingleEvent<T>(Action<Event<T>> checkEventAction)
        where T : IEventPayloadCommon =>
        _helper.ThenGetLatestSingleEvent(checkEventAction);

    public IAggregateTestHelper<TAggregatePayload> ThenLastSingleEventIs<T>(Event<T> @event)
        where T : IEventPayloadCommon =>
        _helper.ThenLastSingleEventIs(@event);

    public IAggregateTestHelper<TAggregatePayload> ThenLastSingleEventPayloadIs<T>(T payload)
        where T : IEventPayloadCommon =>
        _helper.ThenLastSingleEventPayloadIs(payload);

    public IAggregateTestHelper<TAggregatePayload> ThenGetLatestSingleEventPayload<T>(Action<T> checkPayloadAction)
        where T : class, IEventPayloadCommon =>
        _helper.ThenGetLatestSingleEventPayload(checkPayloadAction);

    public IAggregateTestHelper<TAggregatePayload> ThenGetState(
        Action<AggregateState<TAggregatePayload>> checkStateAction) =>
        _helper.ThenGetState(checkStateAction);

    public IAggregateTestHelper<TAggregatePayload> ThenStateIs(AggregateState<TAggregatePayload> expectedState) =>
        _helper.ThenStateIs(expectedState);

    public IAggregateTestHelper<TAggregatePayload> ThenGetPayload(Action<TAggregatePayload> payloadAction) =>
        _helper.ThenGetPayload(payloadAction);

    public IAggregateTestHelper<TAggregatePayload> ThenPayloadIs(TAggregatePayload payload) =>
        _helper.ThenPayloadIs(payload);

    public IAggregateTestHelper<TAggregatePayload> WriteStateToFile(string filename) =>
        _helper.WriteStateToFile(filename);

    public IAggregateTestHelper<TAggregatePayload> WritePayloadToFile(string filename) =>
        _helper.WriteStateToFile(filename);

    public IAggregateTestHelper<TAggregatePayload> ThenStateIsFromJson(string stateJson) =>
        _helper.ThenStateIsFromJson(stateJson);

    public IAggregateTestHelper<TAggregatePayload> ThenStateIsFromFile(string stateFileName) =>
        _helper.ThenStateIsFromFile(stateFileName);

    public IAggregateTestHelper<TAggregatePayload> ThenPayloadIsFromJson(string payloadJson) =>
        _helper.ThenPayloadIsFromJson(payloadJson);

    public IAggregateTestHelper<TAggregatePayload> ThenPayloadIsFromFile(string payloadFileName) =>
        _helper.ThenPayloadIsFromFile(payloadFileName);

    public Guid GetAggregateId() => _helper.GetAggregateId();
    public string GetRootPartitionKey() => _helper.GetRootPartitionKey();

    public int GetCurrentVersion() => _helper.GetCurrentVersion();

    public AggregateState<TAggregatePayload> GetAggregateState() => _helper.GetAggregateState();

    public IAggregateTestHelper<TAggregatePayload> ThenThrows<T>() where T : Exception => _helper.ThenThrows<T>();

    public IAggregateTestHelper<TAggregatePayload> ThenGetException<T>(Action<T> checkException) where T : Exception =>
        _helper.ThenGetException(checkException);

    public IAggregateTestHelper<TAggregatePayload> ThenGetException(Action<Exception> checkException) =>
        _helper.ThenGetException(checkException);

    public IAggregateTestHelper<TAggregatePayload> ThenNotThrowsAnException() => _helper.ThenNotThrowsAnException();

    public IAggregateTestHelper<TAggregatePayload> ThenThrowsAnException() => _helper.ThenThrowsAnException();

    public IAggregateTestHelper<TAggregatePayload> ThenHasValidationErrors(
        IEnumerable<SekibanValidationParameterError> validationParameterErrors) =>
        _helper.ThenHasValidationErrors(validationParameterErrors);

    public IAggregateTestHelper<TAggregatePayload> ThenHasValidationErrors() => _helper.ThenHasValidationErrors();
    public IAggregateTestHelper<TAggregatePayload> WhenSubtypeCommand<TAggregateSubtype>(
        ICommand<TAggregateSubtype> command)
        where TAggregateSubtype : IAggregateSubtypePayloadParentApplicable<TAggregatePayload> =>
        _helper.WhenSubtypeCommand(command);
    public IAggregateTestHelper<TAggregatePayload> WhenSubtypeCommandWithPublish<TAggregateSubtype>(
        ICommand<TAggregateSubtype> command)
        where TAggregateSubtype : IAggregateSubtypePayloadParentApplicable<TAggregatePayload> =>
        _helper.WhenSubtypeCommandWithPublish(command);

    public void Dispose()
    {
        ThrowIfTestHasUnhandledErrors();
    }


    protected virtual void SetupDependency(IServiceCollection serviceCollection)
    {
    }

    public T GetService<T>()
    {
        var toreturn = _serviceProvider.GetService<T>() ??
            throw new SekibanTypeNotFoundException("The object has not been registered." + typeof(T));
        return toreturn;
    }

    #region SingleProjection
    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionStateIs<TSingleProjectionPayload>(
        SingleProjectionState<TSingleProjectionPayload> state)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon =>
        _helper.ThenSingleProjectionStateIs(state);

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionPayloadIs<TSingleProjectionPayload>(
        TSingleProjectionPayload payload) where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon =>
        _helper.ThenSingleProjectionPayloadIs(payload);

    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleProjectionPayload<TSingleProjectionPayload>(
        Action<TSingleProjectionPayload> payloadAction)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon =>
        _helper.ThenGetSingleProjectionPayload(payloadAction);

    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleProjectionState<TSingleProjectionPayload>(
        Action<SingleProjectionState<TSingleProjectionPayload>> stateAction)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon =>
        _helper.ThenGetSingleProjectionState(stateAction);

    public IAggregateTestHelper<TAggregatePayload>
        ThenSingleProjectionPayloadIsFromJson<TSingleProjectionPayload>(string payloadJson)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon =>
        _helper.ThenSingleProjectionPayloadIsFromJson<TSingleProjectionPayload>(payloadJson);

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionPayloadIsFromFile<TSingleProjectionPayload>(
        string payloadFilename) where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon =>
        _helper.ThenSingleProjectionPayloadIsFromFile<TSingleProjectionPayload>(payloadFilename);

    public IAggregateTestHelper<TAggregatePayload>
        WriteSingleProjectionStateToFile<TSingleProjectionPayload>(string filename)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon =>
        _helper.WriteSingleProjectionStateToFile<TSingleProjectionPayload>(filename);
    #endregion


    #region General List Query Test
    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIs<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        ListQueryResult<TQueryResponse> expectedResponse) where TQueryResponse : IQueryResponse =>
        _helper.ThenQueryResponseIs(param, expectedResponse);
    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIs<TQueryResponse>(
        INextListQueryCommon<TQueryResponse> param,
        ListQueryResult<TQueryResponse> expectedResponse) where TQueryResponse : notnull =>
        _helper.ThenQueryResponseIs(param, expectedResponse);
    public IAggregateTestHelper<TAggregatePayload> WriteQueryResponseToFile<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        string filename) where TQueryResponse : IQueryResponse =>
        _helper.WriteQueryResponseToFile(param, filename);
    public IAggregateTestHelper<TAggregatePayload> ThenGetQueryResponse<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        Action<ListQueryResult<TQueryResponse>> responseAction) where TQueryResponse : IQueryResponse =>
        _helper.ThenGetQueryResponse(param, responseAction);
    public IAggregateTestHelper<TAggregatePayload> ThenGetQueryResponse<TQueryResponse>(
        INextListQueryCommon<TQueryResponse> param,
        Action<ListQueryResult<TQueryResponse>> responseAction) where TQueryResponse : notnull =>
        _helper.ThenGetQueryResponse(param, responseAction);

    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIsFromJson<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        string responseJson) where TQueryResponse : IQueryResponse =>
        _helper.ThenQueryResponseIsFromJson(param, responseJson);
    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIsFromJson<TQueryResponse>(
        INextListQueryCommon<TQueryResponse> param,
        string responseJson) where TQueryResponse : IQueryResponse =>
        _helper.ThenQueryResponseIsFromJson(param, responseJson);

    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIsFromFile<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        string responseFilename) where TQueryResponse : IQueryResponse =>
        _helper.ThenQueryResponseIsFromFile(param, responseFilename);
    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIsFromFile<TQueryResponse>(
        INextListQueryCommon<TQueryResponse> param,
        string responseFilename) where TQueryResponse : IQueryResponse =>
        _helper.ThenQueryResponseIsFromFile(param, responseFilename);
    public IAggregateTestHelper<TAggregatePayload> ThenQueryThrows<T>(IListQueryInputCommon param)
        where T : Exception =>
        _helper.ThenQueryThrows<T>(param);
    public IAggregateTestHelper<TAggregatePayload> ThenQueryThrows<T>(INextListQueryCommon param) where T : Exception =>
        _helper.ThenQueryThrows<T>(param);
    public IAggregateTestHelper<TAggregatePayload> ThenQueryGetException<T>(
        IListQueryInputCommon param,
        Action<T> checkException) where T : Exception =>
        _helper.ThenQueryGetException(param, checkException);
    public IAggregateTestHelper<TAggregatePayload> ThenQueryGetException<T>(
        INextListQueryCommon param,
        Action<T> checkException) where T : Exception => _helper.ThenQueryGetException(param, checkException);
    public IAggregateTestHelper<TAggregatePayload> ThenQueryGetException<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        Action<Exception> checkException) where TQueryResponse : IQueryResponse =>
        _helper.ThenQueryGetException(param, checkException);
    public IAggregateTestHelper<TAggregatePayload> ThenQueryGetException<TQueryResponse>(
        INextListQueryCommon<TQueryResponse> param,
        Action<Exception> checkException) where TQueryResponse : IQueryResponse =>
        _helper.ThenQueryGetException(param, checkException);
    public IAggregateTestHelper<TAggregatePayload> ThenQueryNotThrowsAnException(IListQueryInputCommon param) =>
        _helper.ThenQueryNotThrowsAnException(param);
    public IAggregateTestHelper<TAggregatePayload> ThenQueryNotThrowsAnException(INextListQueryCommon param) =>
        _helper.ThenQueryNotThrowsAnException(param);
    public IAggregateTestHelper<TAggregatePayload> ThenQueryThrowsAnException(IListQueryInputCommon param) =>
        _helper.ThenQueryThrowsAnException(param);
    public IAggregateTestHelper<TAggregatePayload> ThenQueryThrowsAnException(INextListQueryCommon param) =>
        _helper.ThenQueryThrowsAnException(param);
    public TQueryResponse GetQueryResponse<TQueryResponse>(IQueryInput<TQueryResponse> param)
        where TQueryResponse : IQueryResponse =>
        _helper.GetQueryResponse(param);
    public TQueryResponse GetQueryResponse<TQueryResponse>(INextQueryCommon<TQueryResponse> param)
        where TQueryResponse : notnull =>
        _helper.GetQueryResponse(param);
    public ListQueryResult<TQueryResponse> GetQueryResponse<TQueryResponse>(IListQueryInput<TQueryResponse> param)
        where TQueryResponse : IQueryResponse =>
        _helper.GetQueryResponse(param);
    public ListQueryResult<TQueryResponse> GetQueryResponse<TQueryResponse>(INextListQueryCommon<TQueryResponse> param)
        where TQueryResponse : notnull =>
        _helper.GetQueryResponse(param);
    #endregion

    #region Query Test (not list)
    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIs<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        TQueryResponse expectedResponse) where TQueryResponse : IQueryResponse =>
        _helper.ThenQueryResponseIs(param, expectedResponse);
    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIs<TQueryResponse>(
        INextQueryCommon<TQueryResponse> param,
        TQueryResponse expectedResponse) where TQueryResponse : notnull =>
        _helper.ThenQueryResponseIs(param, expectedResponse);
    public IAggregateTestHelper<TAggregatePayload> WriteQueryResponseToFile<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        string filename) where TQueryResponse : IQueryResponse =>
        _helper.WriteQueryResponseToFile(param, filename);
    public IAggregateTestHelper<TAggregatePayload> ThenGetQueryResponse<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        Action<TQueryResponse> responseAction) where TQueryResponse : IQueryResponse =>
        _helper.ThenGetQueryResponse(param, responseAction);
    public IAggregateTestHelper<TAggregatePayload> ThenGetQueryResponse<TQueryResponse>(
        INextQueryCommon<TQueryResponse> param,
        Action<TQueryResponse> responseAction) where TQueryResponse : notnull =>
        _helper.ThenGetQueryResponse(param, responseAction);

    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIsFromJson<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        string responseJson) where TQueryResponse : IQueryResponse =>
        _helper.ThenQueryResponseIsFromJson(param, responseJson);

    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIsFromFile<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        string responseFilename) where TQueryResponse : IQueryResponse =>
        _helper.ThenQueryResponseIsFromFile(param, responseFilename);
    public IAggregateTestHelper<TAggregatePayload> ThenQueryThrows<T>(IQueryInputCommon param) where T : Exception =>
        _helper.ThenQueryThrows<T>(param);
    public IAggregateTestHelper<TAggregatePayload> ThenQueryGetException<T>(
        IQueryInputCommon param,
        Action<T> checkException) where T : Exception =>
        _helper.ThenQueryGetException(param, checkException);
    public IAggregateTestHelper<TAggregatePayload> ThenQueryGetException(
        IQueryInputCommon param,
        Action<Exception> checkException) =>
        _helper.ThenQueryGetException(param, checkException);
    public IAggregateTestHelper<TAggregatePayload> ThenQueryNotThrowsAnException(IQueryInputCommon param) =>
        _helper.ThenQueryNotThrowsAnException(param);
    public IAggregateTestHelper<TAggregatePayload> ThenQueryThrowsAnException(IQueryInputCommon param) =>
        _helper.ThenQueryThrowsAnException(param);
    #endregion
}
