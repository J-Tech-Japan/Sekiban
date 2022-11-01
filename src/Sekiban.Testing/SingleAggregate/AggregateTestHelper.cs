using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Document;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Shared;
using Sekiban.Core.Validation;
using Sekiban.Testing.Command;
using System.Text.Json;
using Xunit;
namespace Sekiban.Testing.SingleAggregate;

public class AggregateTestHelper<TAggregatePayload> : IAggregateTestHelper<TAggregatePayload>
    where TAggregatePayload : IAggregatePayload, new()
{
    private readonly TestCommandExecutor _commandExecutor;
    private readonly IServiceProvider _serviceProvider;

    public AggregateTestHelper(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _projector = new DefaultSingleProjector<TAggregatePayload>();
        _aggregate = _projector.CreateInitialAggregate(Guid.Empty);
        _commandExecutor = new TestCommandExecutor(_serviceProvider);
    }
    private Aggregate<TAggregatePayload> _aggregate
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

    private List<SingleAggregateTestBase> SingleProjections
    {
        get;
    } = new();
    public TSingleProjection SetupSingleProjection<TSingleProjection>()
        where TSingleProjection : SingleAggregateTestBase
    {
        var singleProjection = Activator.CreateInstance(typeof(TSingleProjection), _serviceProvider) as TSingleProjection;
        if (singleProjection == null) { throw new Exception("Could not create single aggregate projection"); }
        SingleProjections.Add(singleProjection);
        return singleProjection;
    }
    public IAggregateTestHelper<TAggregatePayload> GivenScenario(Action initialAction)
    {
        initialAction();
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEvent(IEvent ev)
    {
        var documentWriter = _serviceProvider.GetRequiredService(typeof(IDocumentWriter)) as IDocumentWriter;
        if (documentWriter is null) { throw new Exception("Failed to get document writer"); }
        documentWriter.SaveAsync(ev, typeof(TAggregatePayload)).Wait();
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEvents(IEnumerable<IEvent> events)
    {
        foreach (var ev in events)
        {
            GivenEnvironmentEvent(ev);
        }
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsFile(string filename)
    {
        using var openStream = File.OpenRead(filename);
        var list = JsonSerializer.Deserialize<List<JsonElement>>(openStream);
        if (list is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        AddEventsFromList(list);
        return this;
    }
    public AggregateState<TEnvironmentAggregatePayload>
        GetEnvironmentAggregateState<TEnvironmentAggregatePayload>(Guid aggregateId)
        where TEnvironmentAggregatePayload : IAggregatePayload, new()
    {
        var singleAggregateService = _serviceProvider.GetRequiredService(typeof(ISingleProjectionService)) as ISingleProjectionService;
        if (singleAggregateService is null) { throw new Exception("Failed to get single aggregate service"); }
        var aggregate = singleAggregateService.GetAggregateStateAsync<TEnvironmentAggregatePayload>(aggregateId).Result;
        return aggregate ?? throw new SekibanAggregateNotExistsException(aggregateId, typeof(TEnvironmentAggregatePayload).Name);
    }
    public IReadOnlyCollection<IEvent> GetLatestEnvironmentEvents() => _commandExecutor.LatestEvents;

    public IAggregateTestHelper<TAggregatePayload> WhenCreate<C>(C createCommand) where C : ICreateCommand<TAggregatePayload>
    {
        ResetBeforeCommand();

        var validationResults = createCommand.TryValidateProperties().ToList();
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
            _aggregate = new Aggregate<TAggregatePayload> { AggregateId = aggregateId };
            var result = handler.HandleAsync(commandDocument, _aggregate).Result;
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
        if (_latestEvents.Any(
            ev => (ev == _latestEvents.First() && !ev.IsAggregateInitialEvent) || (ev != _latestEvents.First() && ev.IsAggregateInitialEvent)))
        {
            throw new SekibanCreateCommandShouldSaveCreateEventFirstException();
        }
        SingleProjections.ForEach(m => m.SetAggregateId(_aggregate.AggregateId));
        SaveEvents(_latestEvents);
        CheckCommandJSONSupports(commandDocument);
        CheckStateJSONSupports();
        return this;
    }

    public IAggregateTestHelper<TAggregatePayload> WhenChange<C>(C changeCommand) where C : ChangeCommandBase<TAggregatePayload>
    {
        return WhenChange(_ => changeCommand);
    }
    public IAggregateTestHelper<TAggregatePayload> WhenChange<C>(Func<AggregateState<TAggregatePayload>, C> commandFunc)
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
        var command = commandFunc(_aggregate.ToState());
        var validationResults = command.TryValidateProperties().ToList();
        if (validationResults.Any())
        {
            _latestValidationErrors = SekibanValidationParameterError.CreateFromValidationResults(validationResults).ToList();
            return this;
        }
        var commandDocument = new CommandDocument<C>(_aggregate.AggregateId, command, typeof(TAggregatePayload));
        if (command is not IOnlyPublishingCommand)
        {
            try
            {
                var response = handler.HandleAsync(commandDocument, _aggregate).Result;
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
                var result = handler.HandleForOnlyPublishingCommandAsync(commandDocument, _aggregate.AggregateId).Result;
                _latestEvents = result.Events.ToList();
                foreach (var ev in _latestEvents)
                {
                    _aggregate.ApplyEvent(ev);
                }
            }
            catch (Exception ex)
            {
                _latestException = ex;
                return this;
            }
            CheckCommandJSONSupports(commandDocument);
        }
        foreach (var ev in _latestEvents)
        {
            if (ev.IsAggregateInitialEvent) { throw new SekibanChangeCommandShouldNotSaveCreateEventException(); }
        }
        SaveEvents(_latestEvents);
        CheckStateJSONSupports();
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenGetEvents(Action<List<IEvent>> checkEventsAction)
    {
        checkEventsAction(_latestEvents);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenSingleEventIs<T>(Event<T> @event) where T : IEventPayload
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
    public IAggregateTestHelper<TAggregatePayload> ThenSingleEventPayloadIs<T>(T payload) where T : IEventPayload
    {
        if (_latestEvents.Count != 1) { throw new SekibanInvalidArgumentException(); }
        Assert.IsType<Event<T>>(_latestEvents.First());

        var actualJson = SekibanJsonHelper.Serialize(_latestEvents.First().GetPayload());
        var expectedJson = SekibanJsonHelper.Serialize(payload);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleEventPayload<T>(Action<T> checkPayloadAction) where T : class, IEventPayload
    {
        if (_latestEvents.Count != 1) { throw new SekibanInvalidArgumentException(); }
        Assert.IsType<T>(_latestEvents.First().GetPayload());
        checkPayloadAction(_latestEvents.First().GetPayload() as T ?? throw new SekibanInvalidEventException());
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenGetState(Action<AggregateState<TAggregatePayload>> checkStateAction)
    {
        checkStateAction(_aggregate.ToState());
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenStateIs(AggregateState<TAggregatePayload> expectedState)
    {
        var actual = _aggregate.ToState();
        var expected = expectedState.GetComparableObject(actual);
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenGetPayload(Action<TAggregatePayload> payloadAction)
    {
        payloadAction(_aggregate.ToState().Payload);
        return this;
    }

    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleEvent<T>(Action<T> checkEventAction) where T : IEvent
    {
        if (_latestEvents.Count != 1) { throw new SekibanInvalidArgumentException(); }
        Assert.IsType<T>(_latestEvents.First());
        checkEventAction((T)_latestEvents.First());
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenPayloadIs(TAggregatePayload payload)
    {
        var actual = _aggregate.ToState().Payload;
        var expected = payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> WriteStateToFile(string filename)
    {
        var actual = _aggregate.ToState();
        var actualJson = SekibanJsonHelper.Serialize(actual);
        File.WriteAllText(filename, actualJson);
        return this;
    }
    public Guid GetAggregateId() => _aggregate.AggregateId;
    public int GetCurrentVersion() => _aggregate.Version;
    public AggregateState<TAggregatePayload> GetAggregateState() => _aggregate.ToState();
    public Aggregate<TAggregatePayload> GetAggregate() => _aggregate;
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
        var actual = _aggregate.ToState().Payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        File.WriteAllText(filename, actualJson);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenStateIsFromJson(string stateJson)
    {
        var state = JsonSerializer.Deserialize<AggregateState<TAggregatePayload>>(stateJson);
        if (state is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        var actual = _aggregate.ToState();
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
        var actual = _aggregate.ToState();
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
        var actual = _aggregate.ToState().Payload;
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
        var actual = _aggregate.ToState().Payload;
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
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentCommandExecutorAction(Action<TestCommandExecutor> action)
    {
        action(_commandExecutor);
        return this;
    }
    private void SaveEvents(IEnumerable<IEvent> events)
    {
        foreach (var ev in events)
        {
            GivenEnvironmentEvent(ev);
        }
    }
    private void AddEventsFromList(List<JsonElement> list)
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
            GivenEnvironmentEvent((Event<IEventPayload>)eventInstance);
        }
    }

    private void CheckStateJSONSupports()
    {
        var state = _aggregate.ToState();
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
}
