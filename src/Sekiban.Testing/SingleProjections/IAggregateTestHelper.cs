using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Documents;
using Sekiban.Core.Events;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Usecase;
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
    public Guid RunEnvironmentCommand<TEnvironmentAggregatePayload>(
        ICommandCommon<TEnvironmentAggregatePayload> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregatePayload : IAggregatePayloadCommon;
    /// <summary>
    ///     Run a command in environment as given condition (but not for the aggregate that will be tested)
    /// </summary>
    /// <param name="command"></param>
    /// <param name="injectingAggregateId"></param>
    /// <typeparam name="TEnvironmentAggregatePayload"></typeparam>
    /// <returns></returns>
    public Guid GivenEnvironmentCommand<TEnvironmentAggregatePayload>(
        ICommandCommon<TEnvironmentAggregatePayload> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregatePayload : IAggregatePayloadCommon;
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
    ///     (To test subscription values, use this method. But be careful, orders could be different from actual
    ///     execution)
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
    ///     (To test subscription values, use this method. But be careful, orders could be different from actual
    ///     execution)
    /// </summary>
    /// <param name="events"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsWithPublishAndBlockingEvents(
        IEnumerable<IEvent> events);
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
    ///     (To test subscription values, use this method. But be careful, orders could be different from actual
    ///     execution)
    /// </summary>
    /// <param name="filename"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsFileWithPublishAndBlockingEvents(
        string filename);
    /// <summary>
    ///     Run command in environment (but not for the aggregate that will be tested) and publish to the system.
    /// </summary>
    /// <param name="command"></param>
    /// <param name="injectingAggregateId"></param>
    /// <typeparam name="TEnvironmentAggregatePayload"></typeparam>
    /// <returns></returns>
    public Guid RunEnvironmentCommandWithPublish<TEnvironmentAggregatePayload>(
        ICommandCommon<TEnvironmentAggregatePayload> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregatePayload : IAggregatePayloadCommon;
    /// <summary>
    ///     Run command in environment as given condition (but not for the aggregate that will be tested) and publish to
    ///     the
    ///     system.
    /// </summary>
    /// <param name="command"></param>
    /// <param name="injectingAggregateId"></param>
    /// <typeparam name="TEnvironmentAggregatePayload"></typeparam>
    /// <returns></returns>
    public Guid GivenEnvironmentCommandWithPublish<TEnvironmentAggregatePayload>(
        ICommandCommon<TEnvironmentAggregatePayload> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregatePayload : IAggregatePayloadCommon;
    /// <summary>
    ///     Run command in environment (but not for the aggregate that will be tested) and publish to the system.
    ///     Even non blocking subscriptions will be executed by same thread and block the execution
    ///     (To test subscription values, use this method. But be careful, orders could be different from actual
    ///     execution)
    /// </summary>
    /// <param name="command"></param>
    /// <param name="injectingAggregateId"></param>
    /// <typeparam name="TEnvironmentAggregatePayload"></typeparam>
    /// <returns></returns>
    public Guid RunEnvironmentCommandWithPublishAndBlockingEvent<TEnvironmentAggregatePayload>(
        ICommandCommon<TEnvironmentAggregatePayload> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregatePayload : IAggregatePayloadCommon;
    /// <summary>
    ///     Run command in environment as given condition (but not for the aggregate that will be tested) and publish to
    ///     the
    ///     system.
    ///     Even non blocking subscriptions will be executed by same thread and block the execution
    ///     (To test subscription values, use this method. But be careful, orders could be different from actual
    ///     execution)
    /// </summary>
    /// <param name="command"></param>
    /// <param name="injectingAggregateId"></param>
    /// <typeparam name="TEnvironmentAggregatePayload"></typeparam>
    /// <returns></returns>
    public Guid GivenEnvironmentCommandWithPublishAndBlockingEvent<TEnvironmentAggregatePayload>(
        ICommandCommon<TEnvironmentAggregatePayload> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregatePayload : IAggregatePayloadCommon;
    /// <summary>
    ///     Run action with command executor
    /// </summary>
    /// <param name="action"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentCommandExecutorAction(
        Action<TestCommandExecutor> action);
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


    /// <summary>
    ///     Run a command
    ///     Use this method to prepare for the sut (system under test)
    ///     Feature will be same as WhenCommand
    /// </summary>
    /// <param name="command"></param>
    /// <typeparam name="TCommand"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> GivenCommand<TCommand>(TCommand command)
        where TCommand : ICommandCommon<TAggregatePayload>;
    /// <summary>
    ///     Run a command with Subtype, event will not be published
    ///     Use this method to prepare for the sut (system under test)
    ///     Feature will be same as WhenSubtypeCommand
    /// </summary>
    /// <param name="command"></param>
    /// <typeparam name="TAggregateSubtype"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> GivenSubtypeCommand<TAggregateSubtype>(
        ICommandCommon<TAggregateSubtype> command)
        where TAggregateSubtype : IAggregateSubtypePayloadParentApplicable<TAggregatePayload>;
    /// <summary>
    ///     Run a command with Subtype, event will be published
    ///     Use this method to prepare for the sut (system under test)
    ///     Feature will be same as WhenCommand
    /// </summary>
    /// <param name="command"></param>
    /// <typeparam name="TAggregateSubtype"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> GivenSubtypeCommandWithPublish<TAggregateSubtype>(
        ICommandCommon<TAggregateSubtype> command)
        where TAggregateSubtype : IAggregateSubtypePayloadParentApplicable<TAggregatePayload>;
    /// <summary>
    ///     Run a command with Subtype, event will be published
    ///     Even non blocking subscriptions will be executed by same thread and block the execution
    ///     (To test subscription values, use this method. But be careful, orders could be different from actual
    ///     execution)
    ///     Use this method to prepare for the sut (system under test)
    ///     Feature will be same as WhenSubtypeCommandWithPublishAndBlockingSubscriber
    /// </summary>
    /// <param name="command"></param>
    /// <typeparam name="TAggregateSubtype"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload>
        GivenSubtypeCommandWithPublishAndBlockingSubscriber<TAggregateSubtype>(
            ICommandCommon<TAggregateSubtype> command)
        where TAggregateSubtype : IAggregateSubtypePayloadParentApplicable<TAggregatePayload>;
    /// <summary>
    ///     Run a command.
    ///     Use this method to prepare for the sut (system under test)
    ///     Feature will be same as WhenCommand
    /// </summary>
    /// <param name="commandFunc"></param>
    /// <typeparam name="TCommand"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> GivenCommand<TCommand>(
        Func<AggregateState<TAggregatePayload>, TCommand> commandFunc)
        where TCommand : ICommandCommon<TAggregatePayload>;
    /// <summary>
    ///     Run a Command and publish events.
    ///     When events are published, local subscriber will be executed.
    ///     Use this method to prepare for the sut (system under test)
    ///     Feature will be same as WhenCommandWithPublish
    /// </summary>
    /// <param name="command"></param>
    /// <typeparam name="TCommand"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> GivenCommandWithPublish<TCommand>(TCommand command)
        where TCommand : ICommandCommon<TAggregatePayload>;
    /// <summary>
    ///     Run a Command and publish events.
    ///     When events are published, local subscriber will be executed.
    ///     Even non blocking subscriptions will be executed by same thread and block the execution
    ///     (To test subscription values, use this method. But be careful, orders could be different from actual
    ///     execution)
    ///     Use this method to prepare for the sut (system under test)
    ///     Feature will be same as WhenCommandWithPublishAndBlockingSubscriber
    /// </summary>
    /// <param name="command"></param>
    /// <typeparam name="TCommand"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> GivenCommandWithPublishAndBlockingSubscriber<TCommand>(
        TCommand command) where TCommand : ICommandCommon<TAggregatePayload>;
    /// <summary>
    ///     Run a Command and publish events.
    ///     When events are published, local subscriber will be executed.
    ///     Use this method to prepare for the sut (system under test)
    ///     Feature will be same as WhenCommandWithPublish
    /// </summary>
    /// <param name="commandFunc"></param>
    /// <typeparam name="TCommand"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> GivenCommandWithPublish<TCommand>(
        Func<AggregateState<TAggregatePayload>, TCommand> commandFunc)
        where TCommand : ICommandCommon<TAggregatePayload>;
    /// <summary>
    ///     Run a Command and publish events.
    ///     When events are published, local subscriber will be executed.
    ///     Even non blocking subscriptions will be executed by same thread and block the execution
    ///     (To test subscription values, use this method. But be careful, orders could be different from actual
    ///     execution)
    ///     Use this method to prepare for the sut (system under test)
    ///     Feature will be same as WhenCommandWithPublishAndBlockingSubscriber
    /// </summary>
    /// <param name="commandFunc"></param>
    /// <typeparam name="TCommand"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> GivenCommandWithPublishAndBlockingSubscriber<TCommand>(
        Func<AggregateState<TAggregatePayload>, TCommand> commandFunc)
        where TCommand : ICommandCommon<TAggregatePayload>;

    public ResultBox<TOut> GivenUsecase<TIn, TOut>(ISekibanUsecaseAsync<TIn, TOut> usecaseAsync)
        where TIn : class, ISekibanUsecaseAsync<TIn, TOut>, IEquatable<TIn> where TOut : notnull =>
        WhenUsecase(usecaseAsync);
    public ResultBox<TOut> GivenUsecase<TIn, TOut>(ISekibanUsecase<TIn, TOut> usecaseAsync)
        where TIn : class, ISekibanUsecase<TIn, TOut>, IEquatable<TIn> where TOut : notnull =>
        WhenUsecase(usecaseAsync);
    #endregion

    #region When
    /// <summary>
    ///     Run a command
    /// </summary>
    /// <param name="command"></param>
    /// <typeparam name="TCommand"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> WhenCommand<TCommand>(TCommand command)
        where TCommand : ICommandCommon<TAggregatePayload>;
    /// <summary>
    ///     Run a command with Subtype, event will not be published
    /// </summary>
    /// <param name="command"></param>
    /// <typeparam name="TAggregateSubtype"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> WhenSubtypeCommand<TAggregateSubtype>(
        ICommandCommon<TAggregateSubtype> command)
        where TAggregateSubtype : IAggregateSubtypePayloadParentApplicable<TAggregatePayload>;
    /// <summary>
    ///     Run a command with Subtype, event will be published
    /// </summary>
    /// <param name="command"></param>
    /// <typeparam name="TAggregateSubtype"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> WhenSubtypeCommandWithPublish<TAggregateSubtype>(
        ICommandCommon<TAggregateSubtype> command)
        where TAggregateSubtype : IAggregateSubtypePayloadParentApplicable<TAggregatePayload>;
    /// <summary>
    ///     Run a command with Subtype, event will be published
    ///     Even non blocking subscriptions will be executed by same thread and block the execution
    ///     (To test subscription values, use this method. But be careful, orders could be different from actual
    ///     execution)
    /// </summary>
    /// <param name="command"></param>
    /// <typeparam name="TAggregateSubtype"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload>
        WhenSubtypeCommandWithPublishAndBlockingSubscriber<TAggregateSubtype>(ICommandCommon<TAggregateSubtype> command)
        where TAggregateSubtype : IAggregateSubtypePayloadParentApplicable<TAggregatePayload>;
    /// <summary>
    ///     Run a command.
    /// </summary>
    /// <param name="commandFunc"></param>
    /// <typeparam name="TCommand"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> WhenCommand<TCommand>(
        Func<AggregateState<TAggregatePayload>, TCommand> commandFunc)
        where TCommand : ICommandCommon<TAggregatePayload>;
    /// <summary>
    ///     Run a Command and publish events.
    ///     When events are published, local subscriber will be executed.
    /// </summary>
    /// <param name="command"></param>
    /// <typeparam name="TCommand"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> WhenCommandWithPublish<TCommand>(TCommand command)
        where TCommand : ICommandCommon<TAggregatePayload>;
    /// <summary>
    ///     Run a Command and publish events.
    ///     When events are published, local subscriber will be executed.
    ///     Even non blocking subscriptions will be executed by same thread and block the execution
    ///     (To test subscription values, use this method. But be careful, orders could be different from actual
    ///     execution)
    /// </summary>
    /// <param name="command"></param>
    /// <typeparam name="TCommand"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> WhenCommandWithPublishAndBlockingSubscriber<TCommand>(
        TCommand command) where TCommand : ICommandCommon<TAggregatePayload>;
    /// <summary>
    ///     Run a Command and publish events.
    ///     When events are published, local subscriber will be executed.
    /// </summary>
    /// <param name="commandFunc"></param>
    /// <typeparam name="TCommand"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> WhenCommandWithPublish<TCommand>(
        Func<AggregateState<TAggregatePayload>, TCommand> commandFunc)
        where TCommand : ICommandCommon<TAggregatePayload>;
    /// <summary>
    ///     Run a Command and publish events.
    ///     When events are published, local subscriber will be executed.
    ///     Even non blocking subscriptions will be executed by same thread and block the execution
    ///     (To test subscription values, use this method. But be careful, orders could be different from actual
    ///     execution)
    /// </summary>
    /// <param name="commandFunc"></param>
    /// <typeparam name="TCommand"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> WhenCommandWithPublishAndBlockingSubscriber<TCommand>(
        Func<AggregateState<TAggregatePayload>, TCommand> commandFunc)
        where TCommand : ICommandCommon<TAggregatePayload>;

    public ResultBox<TOut> WhenUsecase<TIn, TOut>(ISekibanUsecaseAsync<TIn, TOut> usecaseAsync)
        where TIn : class, ISekibanUsecaseAsync<TIn, TOut>, IEquatable<TIn> where TOut : notnull;
    public ResultBox<TOut> WhenUsecase<TIn, TOut>(ISekibanUsecase<TIn, TOut> usecaseAsync)
        where TIn : class, ISekibanUsecase<TIn, TOut>, IEquatable<TIn> where TOut : notnull;
    #endregion

    #region Then
    /// <summary>
    /// </summary>
    /// <param name="checkEventsAction"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenGetLatestEvents(Action<List<IEvent>> checkEventsAction);
    /// <summary>
    ///     Get all events to validate
    /// </summary>
    /// <param name="checkEventsAction"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenGetAllAggregateEvents(Action<List<IEvent>> checkEventsAction);
    /// <summary>
    ///     Get latest event to validate
    /// </summary>
    /// <param name="checkEventAction"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenGetLatestSingleEvent<T>(Action<Event<T>> checkEventAction)
        where T : IEventPayloadCommon;
    /// <summary>
    ///     Get Latest event to validate.
    ///     Specify event type, if event was wrong type, it will throw exception.
    /// </summary>
    /// <param name="ev"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenLastSingleEventIs<T>(Event<T> ev)
        where T : IEventPayloadCommon;
    /// <summary>
    ///     Check latest event payload.
    ///     Specify type and compare payload.
    /// </summary>
    /// <param name="payload"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenLastSingleEventPayloadIs<T>(T payload)
        where T : IEventPayloadCommon;
    /// <summary>
    ///     Get latest event payload to validate.
    ///     Specify type and compare payload.
    /// </summary>
    /// <param name="checkPayloadAction"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenGetLatestSingleEventPayload<T>(Action<T> checkPayloadAction)
        where T : class, IEventPayloadCommon;
    /// <summary>
    ///     Get state to validate
    /// </summary>
    /// <param name="checkStateAction"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenGetState(
        Action<AggregateState<TAggregatePayload>> checkStateAction);
    /// <summary>
    ///     Compare state with expected state
    /// </summary>
    /// <param name="expectedState"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenStateIs(AggregateState<TAggregatePayload> expectedState);
    /// <summary>
    ///     Get Aggregate Payload to validate
    /// </summary>
    /// <param name="payloadAction"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenGetPayload(Action<TAggregatePayload> payloadAction);
    /// <summary>
    ///     Compare Aggregate Payload with expected payload
    /// </summary>
    /// <param name="payload"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenPayloadIs(TAggregatePayload payload);
    /// <summary>
    ///     Write Aggregate State to Json file
    /// </summary>
    /// <param name="filename"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> WriteStateToFile(string filename);
    /// <summary>
    ///     Write Aggregate Payload to Json file
    /// </summary>
    /// <param name="filename"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> WritePayloadToFile(string filename);
    /// <summary>
    ///     Compare Aggregate State with Json string
    /// </summary>
    /// <param name="stateJson"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenStateIsFromJson(string stateJson);
    /// <summary>
    ///     Compare Aggregate State with Json file
    /// </summary>
    /// <param name="stateFileName"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenStateIsFromFile(string stateFileName);
    /// <summary>
    ///     Compare Aggregate Payload with Json string
    /// </summary>
    /// <param name="payloadJson"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenPayloadIsFromJson(string payloadJson);
    /// <summary>
    ///     Compare Aggregate Payload with Json file
    /// </summary>
    /// <param name="payloadFileName"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenPayloadIsFromFile(string payloadFileName);
    /// <summary>
    ///     Check if command throws specified exception type.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenThrows<T>() where T : Exception;
    /// <summary>
    ///     Get specified type exception thrown with last command to check exception.
    /// </summary>
    /// <param name="checkException"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenGetException<T>(Action<T> checkException) where T : Exception;
    /// <summary>
    ///     Get exception thrown with last command to check exception.
    /// </summary>
    /// <param name="checkException"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenGetException(Action<Exception> checkException);
    /// <summary>
    ///     Check if command not throws exception this expects no exception
    /// </summary>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenNotThrowsAnException();
    /// <summary>
    ///     Check if command throws exception this expects some exception
    /// </summary>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenThrowsAnException();
    /// <summary>
    ///     Check Validate Errors
    /// </summary>
    /// <param name="validationParameterErrors"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenHasValidationErrors(
        IEnumerable<SekibanValidationParameterError> validationParameterErrors);
    /// <summary>
    ///     Check if validation errors exists, this method expects some validation errors
    /// </summary>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenHasValidationErrors();
    #endregion

    #region Get
    /// <summary>
    ///     Get current aggregate id of Current Aggregate
    ///     Current Aggregate is the aggregate that sent last command
    /// </summary>
    /// <returns></returns>
    public Guid GetAggregateId();
    /// <summary>
    ///     Get current root partition keyof Current Aggregate
    ///     Current Aggregate is the aggregate that sent last command
    /// </summary>
    /// <returns></returns>
    public string GetRootPartitionKey();
    /// <summary>
    ///     Get current version of Current Aggregate
    ///     Current Aggregate is the aggregate that sent last command
    /// </summary>
    /// <returns></returns>
    public int GetCurrentVersion();
    /// <summary>
    ///     Get current Aggregate state of Current Aggregate
    ///     Current Aggregate is the aggregate that sent last command
    /// </summary>
    /// <returns></returns>
    public AggregateState<TAggregatePayload> GetAggregateState();
    /// <summary>
    ///     Get specified aggregate state. This is useful when you want to know some aggregate state.
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="rootPartitionKey"></param>
    /// <typeparam name="TEnvironmentAggregatePayload"></typeparam>
    /// <returns></returns>
    public AggregateState<TEnvironmentAggregatePayload> GetEnvironmentAggregateState<TEnvironmentAggregatePayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey)
        where TEnvironmentAggregatePayload : IAggregatePayloadCommon;
    /// <summary>
    ///     Get last event in general
    /// </summary>
    /// <returns></returns>
    public IReadOnlyCollection<IEvent> GetLatestEnvironmentEvents();
    /// <summary>
    ///     Get latest executed events
    /// </summary>
    /// <returns></returns>
    public List<IEvent> GetLatestEvents();
    /// <summary>
    ///     Get all events for current aggregate.
    /// </summary>
    /// <param name="toVersion"></param>
    /// <returns></returns>
    public List<IEvent> GetAllAggregateEvents(int? toVersion = null);
    #endregion

    #region Single Projection
    /// <summary>
    ///     Check Single Projection State for the current Aggregate
    /// </summary>
    /// <param name="state"></param>
    /// <typeparam name="TSingleProjectionPayload"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionStateIs<TSingleProjectionPayload>(
        SingleProjectionState<TSingleProjectionPayload> state)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon;
    /// <summary>
    ///     Check Single Projection Payload for the current Aggregate
    /// </summary>
    /// <param name="payload"></param>
    /// <typeparam name="TSingleProjectionPayload"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionPayloadIs<TSingleProjectionPayload>(
        TSingleProjectionPayload payload) where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon;
    /// <summary>
    ///     Get Single Projection Payload for the current Aggregate
    /// </summary>
    /// <param name="payloadAction"></param>
    /// <typeparam name="TSingleProjectionPayload"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleProjectionPayload<TSingleProjectionPayload>(
        Action<TSingleProjectionPayload> payloadAction)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon;
    /// <summary>
    ///     Get Single Projection State for the current Aggregate
    /// </summary>
    /// <param name="stateAction"></param>
    /// <typeparam name="TSingleProjectionPayload"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleProjectionState<TSingleProjectionPayload>(
        Action<SingleProjectionState<TSingleProjectionPayload>> stateAction)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon;
    /// <summary>
    ///     Check if Single Projection Payload is expected value from Json
    /// </summary>
    /// <param name="payloadJson"></param>
    /// <typeparam name="TSingleProjectionPayload"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload>
        ThenSingleProjectionPayloadIsFromJson<TSingleProjectionPayload>(string payloadJson)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon;
    /// <summary>
    ///     Check if Single Projection Payload is expected value from Json file
    /// </summary>
    /// <param name="payloadFilename"></param>
    /// <typeparam name="TSingleProjectionPayload"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionPayloadIsFromFile<TSingleProjectionPayload>(
        string payloadFilename) where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon;
    /// <summary>
    ///     Write Single Projection State to Json file
    /// </summary>
    /// <param name="filename"></param>
    /// <typeparam name="TSingleProjectionPayload"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload>
        WriteSingleProjectionStateToFile<TSingleProjectionPayload>(string filename)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon;
    #endregion

    #region General List Query Test
    /// <summary>
    ///     Check If Query Response is expected value
    /// </summary>
    /// <param name="param"></param>
    /// <param name="expectedResponse"></param>
    /// <typeparam name="TQueryResponse"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIs<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        ListQueryResult<TQueryResponse> expectedResponse) where TQueryResponse : IQueryResponse;
    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIs<TQueryResponse>(
        INextListQueryCommonOutput<TQueryResponse> param,
        ListQueryResult<TQueryResponse> expectedResponse) where TQueryResponse : notnull;
    /// <summary>
    ///     Write Query Response to Json file
    /// </summary>
    /// <param name="param"></param>
    /// <param name="filename"></param>
    /// <typeparam name="TQueryResponse"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> WriteQueryResponseToFile<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        string filename) where TQueryResponse : IQueryResponse;
    /// <summary>
    ///     Get Query Response to validate
    /// </summary>
    /// <param name="param"></param>
    /// <param name="responseAction"></param>
    /// <typeparam name="TQueryResponse"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenGetQueryResponse<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        Action<ListQueryResult<TQueryResponse>> responseAction) where TQueryResponse : IQueryResponse;
    public IAggregateTestHelper<TAggregatePayload> ThenGetQueryResponse<TQueryResponse>(
        INextListQueryCommonOutput<TQueryResponse> param,
        Action<ListQueryResult<TQueryResponse>> responseAction) where TQueryResponse : notnull;
    /// <summary>
    ///     Check if Query Response is expected value from Json
    /// </summary>
    /// <param name="param"></param>
    /// <param name="responseJson"></param>
    /// <typeparam name="TQueryResponse"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIsFromJson<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        string responseJson) where TQueryResponse : IQueryResponse;
    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIsFromJson<TQueryResponse>(
        INextListQueryCommonOutput<TQueryResponse> param,
        string responseJson) where TQueryResponse : notnull;
    /// <summary>
    ///     Check if Query Response is expected value from Json file
    /// </summary>
    /// <param name="param"></param>
    /// <param name="responseFilename"></param>
    /// <typeparam name="TQueryResponse"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIsFromFile<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        string responseFilename) where TQueryResponse : IQueryResponse;
    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIsFromFile<TQueryResponse>(
        INextListQueryCommonOutput<TQueryResponse> param,
        string responseFilename) where TQueryResponse : notnull;
    /// <summary>
    ///     Check if query throws an exception with specified type
    /// </summary>
    /// <param name="param"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenQueryThrows<T>(IListQueryInputCommon param) where T : Exception;
    public IAggregateTestHelper<TAggregatePayload> ThenQueryThrows<T>(INextListQueryCommon param) where T : Exception;
    /// <summary>
    ///     Get Query's exception with specific type
    /// </summary>
    /// <param name="param"></param>
    /// <param name="checkException"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenQueryGetException<T>(
        IListQueryInputCommon param,
        Action<T> checkException) where T : Exception;
    public IAggregateTestHelper<TAggregatePayload> ThenQueryGetException<T>(
        INextListQueryCommon param,
        Action<T> checkException) where T : Exception;
    /// <summary>
    ///     Get Query's exception
    /// </summary>
    /// <param name="param"></param>
    /// <param name="checkException"></param>
    /// <typeparam name="TQueryResponse"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenQueryGetException<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        Action<Exception> checkException) where TQueryResponse : IQueryResponse;
    public IAggregateTestHelper<TAggregatePayload> ThenQueryGetException<TQueryResponse>(
        INextListQueryCommonOutput<TQueryResponse> param,
        Action<Exception> checkException) where TQueryResponse : notnull;
    /// <summary>
    ///     Check if query not throws an exception
    /// </summary>
    /// <param name="param"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenQueryNotThrowsAnException(IListQueryInputCommon param);
    public IAggregateTestHelper<TAggregatePayload> ThenQueryNotThrowsAnException(INextListQueryCommon param);
    /// <summary>
    ///     Check if query throws exception
    /// </summary>
    /// <param name="param"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenQueryThrowsAnException(IListQueryInputCommon param);
    public IAggregateTestHelper<TAggregatePayload> ThenQueryThrowsAnException(INextListQueryCommon param);

    public TQueryResponse GetQueryResponse<TQueryResponse>(IQueryInput<TQueryResponse> param)
        where TQueryResponse : IQueryResponse;
    public TQueryResponse GetQueryResponse<TQueryResponse>(INextQueryCommonOutput<TQueryResponse> param)
        where TQueryResponse : notnull;
    public ListQueryResult<TQueryResponse> GetQueryResponse<TQueryResponse>(IListQueryInput<TQueryResponse> param)
        where TQueryResponse : IQueryResponse;
    public ListQueryResult<TQueryResponse> GetQueryResponse<TQueryResponse>(
        INextListQueryCommonOutput<TQueryResponse> param) where TQueryResponse : notnull;
    #endregion

    #region Query Test (not list)
    /// <summary>
    ///     Check if Query Response is expected value
    /// </summary>
    /// <param name="param"></param>
    /// <param name="expectedResponse"></param>
    /// <typeparam name="TQueryResponse"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIs<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        TQueryResponse expectedResponse) where TQueryResponse : IQueryResponse;

    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIs<TQueryResponse>(
        INextQueryCommonOutput<TQueryResponse> param,
        TQueryResponse expectedResponse) where TQueryResponse : notnull;


    /// <summary>
    ///     Write Query Response to Json file
    /// </summary>
    /// <param name="param"></param>
    /// <param name="filename"></param>
    /// <typeparam name="TQueryResponse"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> WriteQueryResponseToFile<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        string filename) where TQueryResponse : IQueryResponse;
    /// <summary>
    ///     Get Query Response to validate
    /// </summary>
    /// <param name="param"></param>
    /// <param name="responseAction"></param>
    /// <typeparam name="TQueryResponse"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenGetQueryResponse<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        Action<TQueryResponse> responseAction) where TQueryResponse : IQueryResponse;
    public IAggregateTestHelper<TAggregatePayload> ThenGetQueryResponse<TQueryResponse>(
        INextQueryCommonOutput<TQueryResponse> param,
        Action<TQueryResponse> responseAction) where TQueryResponse : notnull;
    /// <summary>
    ///     Check if Query Response is expected value from Json
    /// </summary>
    /// <param name="param"></param>
    /// <param name="responseJson"></param>
    /// <typeparam name="TQueryResponse"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIsFromJson<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        string responseJson) where TQueryResponse : IQueryResponse;
    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIsFromJson<TQueryResponse>(
        INextQueryCommonOutput<TQueryResponse> param,
        string responseJson) where TQueryResponse : notnull;
    /// <summary>
    ///     Check if Query Response is expected value from Json file
    /// </summary>
    /// <param name="param"></param>
    /// <param name="responseFilename"></param>
    /// <typeparam name="TQueryResponse"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIsFromFile<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        string responseFilename) where TQueryResponse : IQueryResponse;
    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIsFromFile<TQueryResponse>(
        INextQueryCommonOutput<TQueryResponse> param,
        string responseFilename) where TQueryResponse : notnull;
    /// <summary>
    ///     Check if query throws an exception with specified type
    /// </summary>
    /// <param name="param"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenQueryThrows<T>(IQueryInputCommon param) where T : Exception;
    public IAggregateTestHelper<TAggregatePayload> ThenQueryThrows<T>(INextQueryCommon param) where T : Exception;
    /// <summary>
    ///     Get Query's exception with specific type
    /// </summary>
    /// <param name="param"></param>
    /// <param name="checkException"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenQueryGetException<T>(
        IQueryInputCommon param,
        Action<T> checkException) where T : Exception;
    public IAggregateTestHelper<TAggregatePayload> ThenQueryGetException<T>(
        INextQueryCommon param,
        Action<T> checkException) where T : Exception;
    /// <summary>
    ///     Get Query's exception
    /// </summary>
    /// <param name="param"></param>
    /// <param name="checkException"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenQueryGetException(
        IQueryInputCommon param,
        Action<Exception> checkException);
    public IAggregateTestHelper<TAggregatePayload> ThenQueryGetException(
        INextQueryCommon param,
        Action<Exception> checkException);
    /// <summary>
    ///     Check if query not throws an exception
    /// </summary>
    /// <param name="param"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenQueryNotThrowsAnException(IQueryInputCommon param);
    public IAggregateTestHelper<TAggregatePayload> ThenQueryNotThrowsAnException(INextQueryCommon param);
    /// <summary>
    ///     Check if query throws exception
    /// </summary>
    /// <param name="param"></param>
    /// <returns></returns>
    public IAggregateTestHelper<TAggregatePayload> ThenQueryThrowsAnException(IQueryInputCommon param);
    public IAggregateTestHelper<TAggregatePayload> ThenQueryThrowsAnException(INextQueryCommon param);
    #endregion
}
