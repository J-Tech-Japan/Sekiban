using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Document;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Shared;
using Sekiban.Core.Validation;
using Sekiban.Testing.Command;
using System.Diagnostics;
using System.Text.Json;
using Xunit;
namespace Sekiban.Testing.SingleProjections;

public class AggregateTestHelper<TAggregatePayload> : IAggregateTestHelper<TAggregatePayload>
    where TAggregatePayload : IAggregatePayload, new()
{
    private readonly TestCommandExecutor _commandExecutor;
    private readonly IServiceProvider _serviceProvider;

    public AggregateTestHelper(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _projector = new DefaultSingleProjector<TAggregatePayload>();
        Aggregate = _projector.CreateInitialAggregate(Guid.Empty);
        _commandExecutor = new TestCommandExecutor(_serviceProvider);
    }
    public AggregateTestHelper(IServiceProvider serviceProvider, Guid aggregateId)
    {
        _serviceProvider = serviceProvider;
        _projector = new DefaultSingleProjector<TAggregatePayload>();
        var singleProjectionService = serviceProvider.GetService<IAggregateLoader>();
        Debug.Assert(singleProjectionService != null, nameof(singleProjectionService) + " != null");
        Aggregate = singleProjectionService.AsAggregateAsync<TAggregatePayload>(aggregateId).Result ??
            throw new InvalidOperationException("Aggregate not found for Id" + aggregateId + " and Type " + typeof(TAggregatePayload).Name);
        _commandExecutor = new TestCommandExecutor(_serviceProvider);
    }
    private Aggregate<TAggregatePayload> Aggregate
    {
        get;
        set;
    }
    private Exception? _latestException { get; set; }
    private List<IEvent> _latestEvents { get; set; } = new();
    private List<SekibanValidationParameterError> _latestValidationErrors { get; set; } = new();

    private DefaultSingleProjector<TAggregatePayload> _projector
    {
        get;
    }

    public IAggregateTestHelper<TAggregatePayload> GivenScenario(Action initialAction)
    {
        initialAction();
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEvent(IEvent ev)
    {
        SaveEvent(ev, false);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEvents(IEnumerable<IEvent> events)
    {
        SaveEvents(events, false);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsFile(string filename) => GivenEnvironmentEventsFile(filename, false);
    public AggregateState<TEnvironmentAggregatePayload>
        GetEnvironmentAggregateState<TEnvironmentAggregatePayload>(Guid aggregateId)
        where TEnvironmentAggregatePayload : IAggregatePayload, new()
    {
        var singleProjectionService = _serviceProvider.GetRequiredService(typeof(IAggregateLoader)) as IAggregateLoader;
        if (singleProjectionService is null) { throw new Exception("Failed to get single aggregate service"); }
        var aggregate = singleProjectionService.AsDefaultStateAsync<TEnvironmentAggregatePayload>(aggregateId).Result;
        return aggregate ?? throw new SekibanAggregateNotExistsException(aggregateId, typeof(TEnvironmentAggregatePayload).Name);
    }
    public IReadOnlyCollection<IEvent> GetLatestEnvironmentEvents() => _commandExecutor.LatestEvents;
    public List<IEvent> GetLatestEvents() => _latestEvents.ToList();
    public List<IEvent> GetAllAggregateEvents(int? toVersion = null)
    {
        var aggregateLoader = _serviceProvider.GetRequiredService(typeof(IAggregateLoader)) as IAggregateLoader ??
            throw new Exception("Failed to get aggregate loader");
        return aggregateLoader.AllEventsAsync<TAggregatePayload>(GetAggregateId(), toVersion).Result?.ToList() ?? new List<IEvent>();
    }

    public IAggregateTestHelper<TAggregatePayload> WhenCreate<C>(C createCommand) where C : ICreateCommand<TAggregatePayload> =>
        WhenCreatePrivate(createCommand, false);

    public IAggregateTestHelper<TAggregatePayload> WhenChange<C>(C changeCommand) where C : ChangeCommandBase<TAggregatePayload>
    {
        return WhenChange(_ => changeCommand);
    }
    public IAggregateTestHelper<TAggregatePayload> WhenChange<C>(Func<AggregateState<TAggregatePayload>, C> commandFunc)
        where C : ChangeCommandBase<TAggregatePayload> => WhenChangePrivate(commandFunc, false);

    public IAggregateTestHelper<TAggregatePayload> WhenCreateWithPublish<C>(C createCommand) where C : ICreateCommand<TAggregatePayload> =>
        WhenCreatePrivate(createCommand, true);
    public IAggregateTestHelper<TAggregatePayload> WhenChangeWithPublish<C>(C changeCommand) where C : ChangeCommandBase<TAggregatePayload> =>
        WhenChangePrivate(_ => changeCommand, true);
    public IAggregateTestHelper<TAggregatePayload> WhenChangeWithPublish<C>(Func<AggregateState<TAggregatePayload>, C> commandFunc)
        where C : ChangeCommandBase<TAggregatePayload> => WhenChangePrivate(commandFunc, true);
    public IAggregateTestHelper<TAggregatePayload> ThenGetLatestEvents(Action<List<IEvent>> checkEventsAction)
    {
        checkEventsAction(_latestEvents);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenGetAllAggregateEvents(Action<List<IEvent>> checkEventsAction)
    {
        checkEventsAction(GetAllAggregateEvents());
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenLastSingleEventIs<T>(Event<T> @event) where T : IEventPayload
    {
        if (_latestEvents.Count != 1) { throw new SekibanInvalidArgumentException(); }
        Assert.IsType<T>(_latestEvents.First());
        var actual = _latestEvents.First();
        var expected = @event;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenLastSingleEventPayloadIs<T>(T payload) where T : IEventPayload
    {
        if (_latestEvents.Count != 1) { throw new SekibanInvalidArgumentException(); }
        Assert.IsType<Event<T>>(_latestEvents.First());

        var actualJson = SekibanJsonHelper.Serialize(_latestEvents.First().GetPayload());
        var expectedJson = SekibanJsonHelper.Serialize(payload);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenGetLatestSingleEventPayload<T>(Action<T> checkPayloadAction) where T : class, IEventPayload
    {
        if (_latestEvents.Count != 1) { throw new SekibanInvalidArgumentException(); }
        Assert.IsType<T>(_latestEvents.First().GetPayload());
        checkPayloadAction(_latestEvents.First().GetPayload() as T ?? throw new SekibanInvalidEventException());
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenGetState(Action<AggregateState<TAggregatePayload>> checkStateAction)
    {
        checkStateAction(Aggregate.ToState());
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenStateIs(AggregateState<TAggregatePayload> expectedState)
    {
        var actual = Aggregate.ToState();
        var expected = expectedState.GetComparableObject(actual);
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenGetPayload(Action<TAggregatePayload> payloadAction)
    {
        payloadAction(Aggregate.ToState().Payload);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenPayloadIs(TAggregatePayload payload)
    {
        var actual = Aggregate.ToState().Payload;
        var expected = payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> WriteStateToFile(string filename)
    {
        var actual = Aggregate.ToState();
        var actualJson = SekibanJsonHelper.Serialize(actual);
        File.WriteAllText(filename, actualJson);
        return this;
    }
    public Guid GetAggregateId() => Aggregate.AggregateId;
    public int GetCurrentVersion() => Aggregate.Version;
    public AggregateState<TAggregatePayload> GetAggregateState() => Aggregate.ToState();
    public Aggregate<TAggregatePayload> GetAggregate() => Aggregate;
    public IAggregateTestHelper<TAggregatePayload> ThenThrows<T>() where T : Exception
    {
        var exception = _latestException is AggregateException aggregateException ? aggregateException.InnerExceptions.First() : _latestException;
        Assert.IsType<T>(exception);
        return this;
    }

    public IAggregateTestHelper<TAggregatePayload> ThenGetException<T>(Action<T> checkException) where T : Exception
    {
        var exception = _latestException is AggregateException aggregateException ? aggregateException.InnerExceptions.First() : _latestException;
        Assert.IsType<T>(exception);
        checkException((exception as T)!);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenGetException(Action<Exception> checkException)
    {
        Assert.NotNull(_latestException);
        checkException(_latestException!);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenNotThrowsAnException()
    {
        var exception = _latestException is AggregateException aggregateException ? aggregateException.InnerExceptions.First() : _latestException;
        if (exception is SekibanCommandInconsistentVersionException)
        {
            throw new Exception("Change Command needs to put a reference version");
        }
        Assert.Null(exception);

        Assert.Empty(_latestValidationErrors);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenThrowsAnException()
    {
        var exception = _latestException is AggregateException aggregateException ? aggregateException.InnerExceptions.First() : _latestException;
        Assert.NotNull(exception);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenHasValidationErrors(IEnumerable<SekibanValidationParameterError> validationParameterErrors)
    {
        var actual = _latestValidationErrors;
        var expected = validationParameterErrors;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenHasValidationErrors()
    {
        Assert.NotEmpty(_latestValidationErrors);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> WritePayloadToFile(string filename)
    {
        var actual = Aggregate.ToState().Payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        File.WriteAllText(filename, actualJson);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenStateIsFromJson(string stateJson)
    {
        var state = JsonSerializer.Deserialize<AggregateState<TAggregatePayload>>(stateJson);
        if (state is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        var actual = Aggregate.ToState();
        var expected = state.GetComparableObject(actual);
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenStateIsFromFile(string stateFileName)
    {
        using var openStream = File.OpenRead(stateFileName);
        var state = JsonSerializer.Deserialize<AggregateState<TAggregatePayload>>(openStream);
        if (state is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        var actual = Aggregate.ToState();
        var expected = state.GetComparableObject(actual);
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public IAggregateTestHelper<TAggregatePayload> ThenPayloadIsFromJson(string payloadJson)
    {
        var payload = JsonSerializer.Deserialize<TAggregatePayload>(payloadJson);
        if (payload is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        var actual = Aggregate.ToState().Payload;
        var expected = payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public IAggregateTestHelper<TAggregatePayload> ThenPayloadIsFromFile(string payloadFileName)
    {
        using var openStream = File.OpenRead(payloadFileName);
        var payload = JsonSerializer.Deserialize<TAggregatePayload>(openStream);
        if (payload is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        var actual = Aggregate.ToState().Payload;
        var expected = payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public Guid RunEnvironmentCreateCommand<TEnvironmentAggregatePayload>(
        ICreateCommand<TEnvironmentAggregatePayload> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregatePayload : IAggregatePayload, new()
    {
        var (events, aggregateId) = _commandExecutor.ExecuteCreateCommand(command, injectingAggregateId);
        var aggregateEvents = events?.ToList() ?? new List<IEvent>();
        return aggregateId;
    }
    public void RunEnvironmentChangeCommand<TEnvironmentAggregatePayload>(ChangeCommandBase<TEnvironmentAggregatePayload> command)
        where TEnvironmentAggregatePayload : IAggregatePayload, new()
    {
        var _ = _commandExecutor.ExecuteChangeCommand(command);
    }
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventWithPublish(IEvent ev) => SaveEvent(ev, true);
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsWithPublish(IEnumerable<IEvent> events) => SaveEvents(events, true);
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsFileWithPublish(string filename) =>
        GivenEnvironmentEventsFile(filename, true);
    public Guid RunEnvironmentCreateCommandWithPublish<TEnvironmentAggregatePayload>(
        ICreateCommand<TEnvironmentAggregatePayload> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregatePayload : IAggregatePayload, new()
    {
        var (events, aggregateId) = _commandExecutor.ExecuteCreateCommandWithPublish(command, injectingAggregateId);
        var aggregateEvents = events?.ToList() ?? new List<IEvent>();
        return aggregateId;
    }

    public void RunEnvironmentChangeCommandWithPublish<TEnvironmentAggregatePayload>(ChangeCommandBase<TEnvironmentAggregatePayload> command)
        where TEnvironmentAggregatePayload : IAggregatePayload, new()
    {
        var _ = _commandExecutor.ExecuteChangeCommandWithPublish(command);
    }
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentCommandExecutorAction(Action<TestCommandExecutor> action)
    {
        action(_commandExecutor);
        return this;
    }

    public IAggregateTestHelper<TAggregatePayload> ThenGetLatestSingleEvent<T>(Action<Event<T>> checkEventAction) where T : IEventPayload
    {
        if (_latestEvents.Count != 1) { throw new SekibanInvalidArgumentException(); }
        Assert.IsType<Event<T>>(_latestEvents.First());
        checkEventAction((Event<T>)_latestEvents.First());
        return this;
    }
    private IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsFile(string filename, bool withPublish)
    {
        using var openStream = File.OpenRead(filename);
        var list = JsonSerializer.Deserialize<List<JsonElement>>(openStream);
        if (list is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        AddEventsFromList(list, withPublish);
        return this;
    }
    private IAggregateTestHelper<TAggregatePayload> WhenCreatePrivate<C>(C createCommand, bool withPublish)
        where C : ICreateCommand<TAggregatePayload>
    {
        ResetBeforeCommand();

        var validationResults = createCommand.ValidateProperties().ToList();
        if (validationResults.Any())
        {
            _latestValidationErrors = SekibanValidationParameterError.CreateFromValidationResults(validationResults).ToList();
            return this;
        }
        var handler
            = _serviceProvider.GetService(typeof(ICreateCommandHandler<TAggregatePayload, C>)) as
                ICreateCommandHandler<TAggregatePayload, C>;
        if (handler is null)
        {
            throw new SekibanCommandNotRegisteredException(typeof(C).Name);
        }
        var aggregateId = createCommand.GetAggregateId();
        var commandDocument = new CommandDocument<C>(aggregateId, createCommand, typeof(TAggregatePayload));
        try
        {
            Aggregate = new Aggregate<TAggregatePayload> { AggregateId = aggregateId };
            var result = handler.HandleAsync(commandDocument, Aggregate).Result;
            _latestEvents = result.Events.ToList();
        }
        catch (Exception ex)
        {
            _latestException = ex;
            return this;
        }
        if (_latestEvents.Count == 0)
        {
            throw new SekibanCreateHasToMakeEventException();
        }
        SaveEvents(_latestEvents, withPublish);
        CheckCommandJSONSupports(commandDocument);
        CheckStateJSONSupports();
        return this;
    }

    private IAggregateTestHelper<TAggregatePayload> WhenChangePrivate<C>(Func<AggregateState<TAggregatePayload>, C> commandFunc, bool withPublish)
        where C : ChangeCommandBase<TAggregatePayload>
    {
        ResetBeforeCommand();
        var handler
            = _serviceProvider.GetService(typeof(IChangeCommandHandler<TAggregatePayload, C>)) as
                IChangeCommandHandler<TAggregatePayload, C>;
        if (handler is null)
        {
            throw new SekibanCommandNotRegisteredException(typeof(C).Name);
        }
        var command = commandFunc(Aggregate.ToState());
        var validationResults = command.ValidateProperties().ToList();
        if (validationResults.Any())
        {
            _latestValidationErrors = SekibanValidationParameterError.CreateFromValidationResults(validationResults).ToList();
            return this;
        }
        var commandDocument = new CommandDocument<C>(Aggregate.AggregateId, command, typeof(TAggregatePayload));
        if (command is not IOnlyPublishingCommand)
        {
            try
            {
                var response = handler.HandleAsync(commandDocument, Aggregate).Result;
                _latestEvents = response.Events.ToList();
            }
            catch (Exception ex)
            {
                _latestException = ex;
                return this;
            }
            CheckCommandJSONSupports(commandDocument);

        }
        else
        {
            try
            {
                var result = handler.HandleForOnlyPublishingCommandAsync(commandDocument, Aggregate.AggregateId).Result;
                _latestEvents = result.Events.ToList();
                foreach (var ev in _latestEvents)
                {
                    Aggregate.ApplyEvent(ev);
                }
            }
            catch (Exception ex)
            {
                _latestException = ex;
                return this;
            }
            CheckCommandJSONSupports(commandDocument);
        }
        SaveEvents(_latestEvents, withPublish);
        CheckStateJSONSupports();
        return this;
    }
    private IAggregateTestHelper<TAggregatePayload> SaveEvent(IEvent ev, bool withPublish)
    {
        var documentWriter = _serviceProvider.GetRequiredService(typeof(IDocumentWriter)) as IDocumentWriter;
        if (documentWriter is null) { throw new Exception("Failed to get document writer"); }
        if (withPublish)
        {
            documentWriter.SaveAndPublishEvent(ev, typeof(TAggregatePayload)).Wait();
        }
        else
        {
            documentWriter.SaveAsync(ev, typeof(TAggregatePayload)).Wait();
        }
        return this;
    }
    private IAggregateTestHelper<TAggregatePayload> SaveEvents(IEnumerable<IEvent> events, bool withPublish)
    {
        foreach (var ev in events)
        {
            SaveEvent(ev, withPublish);
        }
        return this;
    }
    private void AddEventsFromList(List<JsonElement> list, bool withPublish)
    {
        var registeredEventTypes = _serviceProvider.GetService<RegisteredEventTypes>();
        if (registeredEventTypes is null) { throw new InvalidOperationException("RegisteredEventTypes が登録されていません。"); }
        foreach (var json in list)
        {
            var documentTypeName = json.GetProperty("DocumentTypeName").ToString();
            var eventPayloadType = registeredEventTypes.RegisteredTypes.FirstOrDefault(e => e.Name == documentTypeName);
            if (eventPayloadType is null)
            {
                throw new InvalidDataException($"イベントタイプ {documentTypeName} は登録されていません。");
            }
            var eventType = typeof(Event<>).MakeGenericType(eventPayloadType);
            if (eventType is null)
            {
                throw new InvalidDataException($"イベント {documentTypeName} の生成に失敗しました。");
            }
            var eventInstance = JsonSerializer.Deserialize(json.ToString(), eventType);
            if (eventInstance is null)
            {
                throw new InvalidDataException($"イベント {documentTypeName} のデシリアライズに失敗しました。");
            }
            SaveEvent((Event<IEventPayload>)eventInstance, withPublish);
        }
    }

    private void CheckStateJSONSupports()
    {
        var state = Aggregate.ToState();
        var fromState = _projector.CreateInitialAggregate(state.AggregateId);
        fromState.ApplySnapshot(state);
        var stateFromSnapshot = fromState.ToState().GetComparableObject(state);
        var actualJson = SekibanJsonHelper.Serialize(state);
        var expectedJson = SekibanJsonHelper.Serialize(stateFromSnapshot);
        Assert.Equal(expectedJson, actualJson);
        var json = SekibanJsonHelper.Serialize(state);
        var stateFromJson = SekibanJsonHelper.Deserialize<AggregateState<TAggregatePayload>>(json);
        var stateFromJsonJson = SekibanJsonHelper.Serialize(stateFromJson);
        Assert.Equal(json, stateFromJsonJson);
        CheckEventJsonCompatibility();
    }

    private void CheckCommandJSONSupports(IDocument command)
    {
        var type = command.GetType();
        var json = SekibanJsonHelper.Serialize(command);
        var eventFromJson = SekibanJsonHelper.Deserialize(json, type);
        var json2 = SekibanJsonHelper.Serialize(eventFromJson);
        Assert.Equal(json, json2);
    }

    private void CheckEventJsonCompatibility()
    {
        foreach (var ev in _latestEvents)
        {
            var type = ev.GetType();
            var json = SekibanJsonHelper.Serialize(ev);
            var eventFromJson = SekibanJsonHelper.Deserialize(json, type);
            var json2 = SekibanJsonHelper.Serialize(eventFromJson);
            Assert.Equal(json, json2);
        }
    }
    private void ResetBeforeCommand()
    {
        _latestValidationErrors = new List<SekibanValidationParameterError>();
        _latestEvents = new List<IEvent>();
        _latestException = null;
    }

    #region Single Projection
    private SingleProjectionState<TSingleProjectionPayload> GetSingleProjectionState<TSingleProjectionPayload>()
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
    {
        var singleProjection = _serviceProvider.GetService<IAggregateLoader>() ??
            throw new Exception("Failed to get single projection service");
        return singleProjection.AsSingleProjectionStateAsync<TSingleProjectionPayload>(GetAggregateId()).Result ??
            throw new Exception("Failed to get single projection state for " + typeof(TSingleProjectionPayload).Name + " and " + GetAggregateId());
    }
    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionStateIs<TSingleProjectionPayload>(
        SingleProjectionState<TSingleProjectionPayload> state) where TSingleProjectionPayload : ISingleProjectionPayload, new()
    {
        var actual = GetSingleProjectionState<TSingleProjectionPayload>();
        var expected = state with
        {
            LastEventId = actual.LastEventId,
            Version = actual.Version,
            AppliedSnapshotVersion = actual.AppliedSnapshotVersion,
            LastSortableUniqueId = actual.LastSortableUniqueId
        };
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionPayloadIs<TSingleProjectionPayload>(TSingleProjectionPayload payload)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
    {
        var actual = GetSingleProjectionState<TSingleProjectionPayload>().Payload;
        var expected = payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleProjectionPayload<TSingleProjectionPayload>(
        Action<TSingleProjectionPayload> payloadAction) where TSingleProjectionPayload : ISingleProjectionPayload, new()
    {
        payloadAction(GetSingleProjectionState<TSingleProjectionPayload>().Payload);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleProjectionState<TSingleProjectionPayload>(
        Action<SingleProjectionState<TSingleProjectionPayload>> stateAction) where TSingleProjectionPayload : ISingleProjectionPayload, new()
    {
        stateAction(GetSingleProjectionState<TSingleProjectionPayload>());
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionPayloadIsFromJson<TSingleProjectionPayload>(string payloadJson)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
    {
        var actual = GetSingleProjectionState<TSingleProjectionPayload>().Payload;
        var payload = JsonSerializer.Deserialize<TSingleProjectionPayload>(payloadJson);
        if (payload is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        var expected = payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionPayloadIsFromFile<TSingleProjectionPayload>(string payloadFilename)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
    {
        using var openStream = File.OpenRead(payloadFilename);
        var actual = GetSingleProjectionState<TSingleProjectionPayload>().Payload;
        var payload = JsonSerializer.Deserialize<TSingleProjectionPayload>(openStream);
        if (payload is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        var expected = payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> WriteSingleProjectionStateToFile<TSingleProjectionPayload>(string filename)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
    {
        var state = GetSingleProjectionState<TSingleProjectionPayload>();
        var json = SekibanJsonHelper.Serialize(state);
        File.WriteAllText(filename, json);
        return this;
    }
    #endregion

    #region Aggregate Query
    private TQueryResponse GetAggregateQueryResponse<TQuery, TQueryParameter, TQueryResponse>(TQueryParameter param)
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var queryService = _serviceProvider.GetService<IQueryExecutor>() ??
            throw new Exception("Failed to get Query service");
        return queryService.ForAggregateQueryAsync<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(param).Result ??
            throw new Exception("Failed to get Aggregate Query Response for " + typeof(TQuery).Name);
    }
    public IAggregateTestHelper<TAggregatePayload> WriteAggregateQueryResponseToFile<TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        string filename)
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var json = SekibanJsonHelper.Serialize(GetAggregateQueryResponse<TQuery, TQueryParameter, TQueryResponse>(param));
        if (string.IsNullOrEmpty(json))
        {
            throw new InvalidDataException("Json is null or empty");
        }
        File.WriteAllTextAsync(filename, json);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenAggregateQueryResponseIs<TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        TQueryResponse expectedResponse) where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var actual = GetAggregateQueryResponse<TQuery, TQueryParameter, TQueryResponse>(param);
        var expected = expectedResponse;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenGetAggregateQueryResponse<TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        Action<TQueryResponse> responseAction) where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        responseAction(GetAggregateQueryResponse<TQuery, TQueryParameter, TQueryResponse>(param)!);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenAggregateQueryResponseIsFromJson<TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        string responseJson) where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var response = JsonSerializer.Deserialize<TQueryResponse>(responseJson);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenAggregateQueryResponseIs<TQuery, TQueryParameter, TQueryResponse>(param, response);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenAggregateQueryResponseIsFromFile<TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        string responseFilename) where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<TQueryResponse>(openStream);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenAggregateQueryResponseIs<TQuery, TQueryParameter, TQueryResponse>(param, response);
        return this;
    }
    #endregion

    #region Aggregate　List Query
    private ListQueryResult<TQueryResponse> GetAggregateListQueryResponse<TQuery, TQueryParameter, TQueryResponse>(TQueryParameter param)
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var queryService = _serviceProvider.GetService<IQueryExecutor>() ??
            throw new Exception("Failed to get Query service");
        return queryService.ForAggregateListQueryAsync<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(param).Result ??
            throw new Exception("Failed to get Aggregate List Query Response for " + typeof(TQuery).Name);
    }
    public IAggregateTestHelper<TAggregatePayload> WriteAggregateListQueryResponseToFile<TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        string filename)
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var json = SekibanJsonHelper.Serialize(GetAggregateListQueryResponse<TQuery, TQueryParameter, TQueryResponse>(param));
        if (string.IsNullOrEmpty(json))
        {
            throw new InvalidDataException("Json is null or empty");
        }
        File.WriteAllTextAsync(filename, json);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenAggregateListQueryResponseIs<TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        ListQueryResult<TQueryResponse> expectedResponse) where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var actual = GetAggregateListQueryResponse<TQuery, TQueryParameter, TQueryResponse>(param);
        var expected = expectedResponse;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenGetAggregateListQueryResponse<TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        Action<ListQueryResult<TQueryResponse>> responseAction) where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        responseAction(GetAggregateListQueryResponse<TQuery, TQueryParameter, TQueryResponse>(param)!);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenAggregateListQueryResponseIsFromJson<TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        string responseJson) where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var response = JsonSerializer.Deserialize<ListQueryResult<TQueryResponse>>(responseJson);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenAggregateListQueryResponseIs<TQuery, TQueryParameter, TQueryResponse>(param, response);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenAggregateListQueryResponseIsFromFile<TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        string responseFilename) where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<ListQueryResult<TQueryResponse>>(openStream);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenAggregateListQueryResponseIs<TQuery, TQueryParameter, TQueryResponse>(param, response);
        return this;
    }
    #endregion

    #region SingleProjection Query
    private TQueryResponse GetSingleProjectionQueryResponse<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(TQueryParameter param)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var queryService = _serviceProvider.GetService<IQueryExecutor>() ??
            throw new Exception("Failed to get Query service");
        return queryService.ForSingleProjectionQueryAsync<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(param).Result ??
            throw new Exception("Failed to get Single Projection Query Response for " + typeof(TQuery).Name);
    }

    public IAggregateTestHelper<TAggregatePayload> WriteSingleProjectionQueryResponseToFile<TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string filename)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var json = SekibanJsonHelper.Serialize(
            GetSingleProjectionQueryResponse<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(param));
        if (string.IsNullOrEmpty(json))
        {
            throw new InvalidDataException("Json is null or empty");
        }
        File.WriteAllTextAsync(filename, json);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionQueryResponseIs<TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        TQueryResponse expectedResponse)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var actual = GetSingleProjectionQueryResponse<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(param);
        var expected = expectedResponse;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleProjectionQueryResponse<TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        Action<TQueryResponse> responseAction)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        responseAction(GetSingleProjectionQueryResponse<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(param));
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionQueryResponseIsFromJson<TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseJson)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var response = JsonSerializer.Deserialize<TQueryResponse>(responseJson);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenSingleProjectionQueryResponseIs<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(param, response);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionQueryResponseIsFromFile<TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseFilename)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<TQueryResponse>(openStream);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenSingleProjectionQueryResponseIs<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(param, response);
        return this;
    }
    #endregion

    #region SingleProjection　List Query
    private ListQueryResult<TQueryResponse> GetSingleProjectionListQueryResponse<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var queryService = _serviceProvider.GetService<IQueryExecutor>() ??
            throw new Exception("Failed to get Query service");
        return queryService.ForSingleProjectionListQueryAsync<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(param).Result ??
            throw new Exception("Failed to get Single Projection List Query Response for " + typeof(TQuery).Name);
    }

    public IAggregateTestHelper<TAggregatePayload> WriteSingleProjectionListQueryResponseToFile<TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string filename)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var json = SekibanJsonHelper.Serialize(
            GetSingleProjectionListQueryResponse<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(param));
        if (string.IsNullOrEmpty(json))
        {
            throw new InvalidDataException("Json is null or empty");
        }
        File.WriteAllTextAsync(filename, json);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionListQueryResponseIs<TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        ListQueryResult<TQueryResponse> expectedResponse)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var actual = GetSingleProjectionListQueryResponse<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(param);
        var expected = expectedResponse;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleProjectionListQueryResponse<TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        Action<ListQueryResult<TQueryResponse>> responseAction)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        responseAction(GetSingleProjectionListQueryResponse<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(param));
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionListQueryResponseIsFromJson<TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseJson)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var response = JsonSerializer.Deserialize<ListQueryResult<TQueryResponse>>(responseJson);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenSingleProjectionListQueryResponseIs<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(param, response);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionListQueryResponseIsFromFile<TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseFilename)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<ListQueryResult<TQueryResponse>>(openStream);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenSingleProjectionListQueryResponseIs<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(param, response);
        return this;
    }
    #endregion
}
