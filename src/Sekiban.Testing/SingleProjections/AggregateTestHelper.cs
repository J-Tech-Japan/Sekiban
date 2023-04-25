using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Documents;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Shared;
using Sekiban.Core.Validation;
using Sekiban.Testing.Command;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Xunit;
namespace Sekiban.Testing.SingleProjections;

public class AggregateTestHelper<TAggregatePayload> : IAggregateTestHelper<TAggregatePayload>
    where TAggregatePayload : IAggregatePayloadCommon
{
    private readonly TestCommandExecutor _commandExecutor;
    private readonly IServiceProvider _serviceProvider;

    public AggregateTestHelper(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _projector = new DefaultSingleProjector<TAggregatePayload>();
        _commandExecutor = new TestCommandExecutor(_serviceProvider);
        var aggregateLoader = _serviceProvider.GetRequiredService<IAggregateLoader>() ?? throw new Exception("AggregateLoader is not registered");
        AggregateIdHolder = new AggregateIdHolder<TAggregatePayload>(aggregateLoader);
    }

    public AggregateTestHelper(IServiceProvider serviceProvider, IAggregateIdHolder aggregateIdHolder)
    {
        AggregateIdHolder = aggregateIdHolder;
        _serviceProvider = serviceProvider;
        _projector = new DefaultSingleProjector<TAggregatePayload>();
        var singleProjectionService = serviceProvider.GetService<IAggregateLoader>();
        Debug.Assert(singleProjectionService != null, nameof(singleProjectionService) + " != null");
        _commandExecutor = new TestCommandExecutor(_serviceProvider);
    }
    private Exception? _latestException { get; set; }
    private List<IEvent> _latestEvents { get; set; } = new();
    private ICommandCommon? _latestCommand { get; set; }
    private List<SekibanValidationParameterError> _latestValidationErrors { get; set; } = new();

    private DefaultSingleProjector<TAggregatePayload> _projector { get; }

    public IAggregateIdHolder AggregateIdHolder { get; }

    public IAggregateTestHelper<TAggregatePayloadExpected> ThenPayloadTypeShouldBe<TAggregatePayloadExpected>()
        where TAggregatePayloadExpected : IAggregatePayloadCommon
    {
        Assert.True(AggregateIdHolder.IsAggregateType<TAggregatePayloadExpected>());
        return new AggregateTestHelper<TAggregatePayloadExpected>(_serviceProvider, AggregateIdHolder);
    }
    public IAggregateTestHelper<TAggregateSubtypePayload> Subtype<TAggregateSubtypePayload>()
        where TAggregateSubtypePayload : IAggregatePayloadCommon, IApplicableAggregatePayload<TAggregatePayload>
    {
        var subTypeTest = new AggregateTestHelper<TAggregateSubtypePayload>(_serviceProvider, AggregateIdHolder);
        return subTypeTest;
    }
    // public IAggregateTestHelper<TAggregatePayload> Subtype<TAggregateSubtypePayload>(
    //     Action<IAggregateTestHelper<TAggregateSubtypePayload>> subtypeTestHelperAction)
    //     where TAggregateSubtypePayload : IAggregatePayloadCommon, IApplicableAggregatePayload<TAggregatePayload>
    // {
    //     var subTypeTest = new AggregateTestHelper<TAggregateSubtypePayload>(_serviceProvider, GetAggregateId());
    //     subtypeTestHelperAction(subTypeTest);
    //     var aggregateLoader = _serviceProvider.GetRequiredService(typeof(IAggregateLoader)) as IAggregateLoader ??
    //         throw new Exception("Failed to get aggregate loader");
    //     Aggregate = aggregateLoader.AsAggregateAsync<TAggregatePayload>(subTypeTest.Aggregate.AggregateId).Result ??
    //         new Aggregate<TAggregatePayload>
    //             { AggregateId = subTypeTest.Aggregate.AggregateId };
    //     return this;
    // }
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

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsFile(string filename)
    {
        return GivenEnvironmentEventsFile(filename, false);
    }

    public AggregateState<TEnvironmentAggregatePayload>
        GetEnvironmentAggregateState<TEnvironmentAggregatePayload>(Guid aggregateId)
        where TEnvironmentAggregatePayload : IAggregatePayloadCommon
    {
        var singleProjectionService = _serviceProvider.GetRequiredService(typeof(IAggregateLoader)) as IAggregateLoader;
        if (singleProjectionService is null)
        {
            throw new Exception("Failed to get single aggregate service");
        }
        var aggregate = singleProjectionService.AsDefaultStateAsync<TEnvironmentAggregatePayload>(aggregateId).Result;
        return aggregate ??
            throw new SekibanAggregateNotExistsException(aggregateId, typeof(TEnvironmentAggregatePayload).Name);
    }

    public IReadOnlyCollection<IEvent> GetLatestEnvironmentEvents()
    {
        return _commandExecutor.LatestEvents;
    }

    public List<IEvent> GetLatestEvents()
    {
        return _latestEvents.ToList();
    }

    public List<IEvent> GetAllAggregateEvents(int? toVersion = null)
    {
        var aggregateLoader = _serviceProvider.GetRequiredService(typeof(IAggregateLoader)) as IAggregateLoader ??
            throw new Exception("Failed to get aggregate loader");
        return aggregateLoader.AllEventsAsync<TAggregatePayload>(GetAggregateId(), toVersion).Result?.ToList() ??
            new List<IEvent>();
    }

    public void ThrowIfTestHasUnhandledErrors()
    {
        if (_latestException != null)
        {
            throw _latestException;
        }
        if (_latestValidationErrors.Any())
        {
            var first = _latestValidationErrors.First();
            throw new Exception(
                $"{_latestCommand?.GetType().Name ?? ""}" +
                first.PropertyName +
                " has validation error " +
                first.ErrorMessages.First());
        }
    }
    public IAggregateTestHelper<TAggregatePayload> WhenCommand<TCommand>(TCommand changeCommand) where TCommand : ICommand<TAggregatePayload>
    {
        return WhenCommand(_ => changeCommand);
    }
    public IAggregateTestHelper<TAggregatePayload> WhenSubtypeCommand<TAggregateSubtypePayload, TCommand>(TCommand changeCommand)
        where TAggregateSubtypePayload : TAggregatePayload, IAggregatePayloadCommon where TCommand : ICommand<TAggregateSubtypePayload>
    {
        return WhenCommandPrivate<TAggregateSubtypePayload, TCommand>(_ => changeCommand, false);
    }

    public IAggregateTestHelper<TAggregatePayload> WhenCommand<TCommand>(
        Func<AggregateState<TAggregatePayload>, TCommand> commandFunc)
        where TCommand : ICommand<TAggregatePayload>
    {
        return WhenCommandPrivate<TAggregatePayload, TCommand>(commandFunc, false);
    }

    public IAggregateTestHelper<TAggregatePayload> WhenCommandWithPublish<TCommand>(TCommand changeCommand)
        where TCommand : ICommand<TAggregatePayload>
    {
        return WhenCommandPrivate<TAggregatePayload, TCommand>(_ => changeCommand, true);
    }

    public IAggregateTestHelper<TAggregatePayload> WhenCommandWithPublish<TCommand>(
        Func<AggregateState<TAggregatePayload>, TCommand> commandFunc)
        where TCommand : ICommand<TAggregatePayload>
    {
        return WhenCommandPrivate<TAggregatePayload, TCommand>(commandFunc, true);
    }

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

    public IAggregateTestHelper<TAggregatePayload> ThenLastSingleEventIs<T>(Event<T> @event)
        where T : IEventPayloadCommon
    {
        if (_latestEvents.Count != 1)
        {
            throw new SekibanInvalidArgumentException();
        }
        Assert.IsType<T>(_latestEvents.First());
        var actual = _latestEvents.First();
        var expected = @event;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public IAggregateTestHelper<TAggregatePayload> ThenLastSingleEventPayloadIs<T>(T payload)
        where T : IEventPayloadCommon
    {
        if (_latestEvents.Count != 1)
        {
            throw new SekibanInvalidArgumentException();
        }
        Assert.IsType<Event<T>>(_latestEvents.First());

        var actualJson = SekibanJsonHelper.Serialize(_latestEvents.First().GetPayload());
        var expectedJson = SekibanJsonHelper.Serialize(payload);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public IAggregateTestHelper<TAggregatePayload> ThenGetLatestSingleEventPayload<T>(Action<T> checkPayloadAction)
        where T : class, IEventPayloadCommon
    {
        if (_latestEvents.Count != 1)
        {
            throw new SekibanInvalidArgumentException();
        }
        Assert.IsType<T>(_latestEvents.First().GetPayload());
        checkPayloadAction(_latestEvents.First().GetPayload() as T ?? throw new SekibanInvalidEventException());
        return this;
    }

    public IAggregateTestHelper<TAggregatePayload> ThenGetState(
        Action<AggregateState<TAggregatePayload>> checkStateAction)
    {
        checkStateAction(GetAggregateState());
        return this;
    }

    public IAggregateTestHelper<TAggregatePayload> ThenStateIs(AggregateState<TAggregatePayload> expectedState)
    {
        var actual = GetAggregateState();
        var expected = expectedState.GetComparableObject(actual);
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public IAggregateTestHelper<TAggregatePayload> ThenGetPayload(Action<TAggregatePayload> payloadAction)
    {
        payloadAction(GetAggregateState().Payload);
        return this;
    }

    public AggregateState<TAggregatePayload> GetAggregateState()
    {
        var aggregateLoader = _serviceProvider.GetRequiredService(typeof(IAggregateLoader)) as IAggregateLoader ??
            throw new Exception("Failed to get aggregate loader");
        var aggregateId = GetAggregateId();
        var aggregate = aggregateLoader.AsDefaultStateAsync<TAggregatePayload>(aggregateId).Result;
        return aggregate ??
            throw new SekibanAggregateNotExistsException(GetAggregateId(), typeof(TAggregatePayload).Name);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenPayloadIs(TAggregatePayload payload)
    {
        var actual = GetAggregateState().Payload;
        var expected = payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public IAggregateTestHelper<TAggregatePayload> WriteStateToFile(string filename)
    {
        var actual = GetAggregateState();
        var actualJson = SekibanJsonHelper.Serialize(actual);
        File.WriteAllText(filename, actualJson);
        return this;
    }

    public Guid GetAggregateId()
    {
        return AggregateIdHolder.AggregateId;
    }

    public int GetCurrentVersion()
    {
        return GetAggregateState().Version;
    }

    public IAggregateTestHelper<TAggregatePayload> ThenThrows<T>() where T : Exception
    {
        var exception = _latestException is AggregateException aggregateException
            ? aggregateException.InnerExceptions.First()
            : _latestException;
        Assert.IsType<T>(exception);
        _latestException = null;
        return this;
    }

    public IAggregateTestHelper<TAggregatePayload> ThenGetException<T>(Action<T> checkException) where T : Exception
    {
        var exception = _latestException is AggregateException aggregateException
            ? aggregateException.InnerExceptions.First()
            : _latestException;
        Assert.IsType<T>(exception);
        checkException((exception as T)!);
        _latestException = null;
        return this;
    }

    public IAggregateTestHelper<TAggregatePayload> ThenGetException(Action<Exception> checkException)
    {
        Assert.NotNull(_latestException);
        checkException(_latestException!);
        _latestException = null;
        return this;
    }

    public IAggregateTestHelper<TAggregatePayload> ThenNotThrowsAnException()
    {
        ThrowIfTestHasUnhandledErrors();
        return this;
    }

    public IAggregateTestHelper<TAggregatePayload> ThenThrowsAnException()
    {
        var exception = _latestException is AggregateException aggregateException
            ? aggregateException.InnerExceptions.First()
            : _latestException;
        Assert.NotNull(exception);
        _latestException = null;
        return this;
    }

    public IAggregateTestHelper<TAggregatePayload> ThenHasValidationErrors(
        IEnumerable<SekibanValidationParameterError> validationParameterErrors)
    {
        var actual = _latestValidationErrors;
        var expected = validationParameterErrors;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        _latestValidationErrors = new List<SekibanValidationParameterError>();
        return this;
    }

    public IAggregateTestHelper<TAggregatePayload> ThenHasValidationErrors()
    {
        Assert.NotEmpty(_latestValidationErrors);
        _latestValidationErrors = new List<SekibanValidationParameterError>();
        return this;
    }

    public IAggregateTestHelper<TAggregatePayload> WritePayloadToFile(string filename)
    {
        var actual = GetAggregateState().Payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        File.WriteAllText(filename, actualJson);
        return this;
    }

    public IAggregateTestHelper<TAggregatePayload> ThenStateIsFromJson(string stateJson)
    {
        var state = JsonSerializer.Deserialize<AggregateState<TAggregatePayload>>(stateJson);
        if (state is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        var actual = GetAggregateState();
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
        if (state is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        var actual = GetAggregateState();
        var expected = state.GetComparableObject(actual);
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public IAggregateTestHelper<TAggregatePayload> ThenPayloadIsFromJson(string payloadJson)
    {
        var payload = JsonSerializer.Deserialize<TAggregatePayload>(payloadJson);
        if (payload is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        var actual = GetAggregateState().Payload;
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
        if (payload is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        var actual = GetAggregateState().Payload;
        var expected = payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public Guid RunEnvironmentCommand<TEnvironmentAggregatePayload>(
        ICommand<TEnvironmentAggregatePayload> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregatePayload : IAggregatePayloadCommon
    {
        return _commandExecutor.ExecuteCommand(command, injectingAggregateId);
    }

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventWithPublish(IEvent ev)
    {
        return SaveEvent(ev, true);
    }

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsWithPublish(IEnumerable<IEvent> events)
    {
        return SaveEvents(events, true);
    }

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsFileWithPublish(string filename)
    {
        return GivenEnvironmentEventsFile(filename, true);
    }

    public Guid RunEnvironmentCommandWithPublish<TEnvironmentAggregatePayload>(
        ICommand<TEnvironmentAggregatePayload> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregatePayload : IAggregatePayloadCommon
    {
        return _commandExecutor.ExecuteCommandWithPublish(command, injectingAggregateId);
    }

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentCommandExecutorAction(
        Action<TestCommandExecutor> action)
    {
        action(_commandExecutor);
        return this;
    }

    public IAggregateTestHelper<TAggregatePayload> ThenGetLatestSingleEvent<T>(Action<Event<T>> checkEventAction)
        where T : IEventPayloadCommon
    {
        if (_latestEvents.Count != 1)
        {
            throw new SekibanInvalidArgumentException();
        }
        Assert.IsType<Event<T>>(_latestEvents.First());
        checkEventAction((Event<T>)_latestEvents.First());
        return this;
    }
    public AggregateState<TAggregatePayload> GetAggregateStateIfNotNullEmptyAggregate()
    {
        var aggregateLoader = _serviceProvider.GetRequiredService(typeof(IAggregateLoader)) as IAggregateLoader ??
            throw new Exception("Failed to get aggregate loader");
        var aggregate = aggregateLoader.AsDefaultStateAsync<TAggregatePayload>(GetAggregateId()).Result;
        return aggregate ??
            new AggregateState<TAggregatePayload>
                { AggregateId = GetAggregateId() };
    }

    private IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsFile(string filename, bool withPublish)
    {
        using var openStream = File.OpenRead(filename);
        var list = JsonSerializer.Deserialize<List<JsonElement>>(openStream);
        if (list is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        AddEventsFromList(list, withPublish);
        return this;
    }

    private IAggregateTestHelper<TAggregatePayload> WhenCommandPrivate<TAggregatePayloadIn, TCommand>(
        Func<AggregateState<TAggregatePayload>, TCommand> commandFunc,
        bool withPublish)
        where TAggregatePayloadIn : IAggregatePayloadCommon
        where TCommand : ICommand<TAggregatePayloadIn>
    {
        ResetBeforeCommand();
        var handler
            = _serviceProvider.GetService(typeof(ICommandHandlerCommon<TAggregatePayloadIn, TCommand>)) as
                ICommandHandlerCommon<TAggregatePayloadIn, TCommand>;
        if (handler is null)
        {
            throw new SekibanCommandNotRegisteredException(typeof(TCommand).Name);
        }
        var command = commandFunc(GetAggregateStateIfNotNullEmptyAggregate());
        _latestCommand = command;
        var validationResults = command.ValidateProperties().ToList();
        if (validationResults.Any())
        {
            _latestValidationErrors =
                SekibanValidationParameterError.CreateFromValidationResults(validationResults).ToList();
            return this;
        }
        AggregateIdHolder.AggregateId = command.GetAggregateId();

        var commandDocument = new CommandDocument<TCommand>(GetAggregateId(), command, typeof(TAggregatePayload));
        CheckCommandJSONSupports(commandDocument);

        var aggregateId = GetAggregateId();
        var aggregateLoader = _serviceProvider.GetRequiredService(typeof(IAggregateLoader)) as IAggregateLoader;
        if (aggregateLoader is null)
        {
            throw new Exception("Failed to get AddAggregate Service");
        }
        try
        {
            if (command is IOnlyPublishingCommandCommon)
            {
                var baseClass = typeof(OnlyPublishingCommandHandlerAdapter<,>);
                var adapterClass = baseClass.MakeGenericType(typeof(TAggregatePayloadIn), command.GetType());
                var adapter = Activator.CreateInstance(adapterClass) ?? throw new Exception("Method not found");
                var method = adapterClass.GetMethod("HandleCommandAsync") ??
                    throw new Exception("HandleCommandAsync not found");
                var commandResponse =
                    (CommandResponse)((dynamic?)method.Invoke(
                            adapter,
                            new object?[] { commandDocument, handler, aggregateId }) ??
                        throw new SekibanCommandHandlerNotMatchException(
                            "Command failed to execute " +
                            command.GetType().Name)).Result;
                _latestEvents = commandResponse.Events.ToList();
            }
            else
            {
                var baseClass = typeof(CommandHandlerAdapter<,>);
                var adapterClass = baseClass.MakeGenericType(typeof(TAggregatePayloadIn), command.GetType());
                var adapter = Activator.CreateInstance(adapterClass, aggregateLoader, false) ??
                    throw new Exception("Adapter not found");

                var method = adapterClass.GetMethod("HandleCommandAsync") ??
                    throw new Exception("HandleCommandAsync not found");

                var commandResponse =
                    (CommandResponse)((dynamic?)method.Invoke(
                            adapter,
                            new object?[] { commandDocument, handler, aggregateId }) ??
                        throw new SekibanCommandHandlerNotMatchException(
                            "Command failed to execute " +
                            command.GetType().Name)).Result;
                _latestEvents = commandResponse.Events.ToList();
            }
        }
        catch (Exception ex)
        {
            _latestException = ex;
            return this;
        }

        SaveEvents(_latestEvents, withPublish);
        CheckStateJSONSupports();
        return this;
    }

    private IAggregateTestHelper<TAggregatePayload> SaveEvent(IEvent ev, bool withPublish)
    {
        var documentWriter = _serviceProvider.GetRequiredService(typeof(IDocumentWriter)) as IDocumentWriter;
        if (documentWriter is null)
        {
            throw new Exception("Failed to get document writer");
        }
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
        if (registeredEventTypes is null)
        {
            throw new InvalidOperationException("RegisteredEventTypes が登録されていません。");
        }
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
            SaveEvent((Event<IEventPayloadCommon>)eventInstance, withPublish);
        }
    }

    private void CheckStateJSONSupports()
    {
        // when aggregate payload type changed, skip this test
        if (!AggregateIdHolder.IsAggregateType<TAggregatePayload>()) { return; }
        var state = GetAggregateState();
        var fromState = _projector.CreateInitialAggregate(state.AggregateId);
        fromState.ApplySnapshot(state);
        var stateFromSnapshot = fromState.ToState().GetComparableObject(state);
        var actualJson = SekibanJsonHelper.Serialize(state);
        var expectedJson = SekibanJsonHelper.Serialize(stateFromSnapshot);
        Assert.Equal(expectedJson, actualJson);
        var json = SekibanJsonHelper.Serialize(state.AsDynamicTypedState());

        var method = typeof(SekibanJsonHelper)
            .GetMethods(BindingFlags.Static | BindingFlags.Public)
            .FirstOrDefault(m => m.Name == "Deserialize" && m.GetParameters().Length == 1);
        var type = typeof(AggregateState<>);
        var genericType = type.MakeGenericType(state.Payload.GetType());
        var genericMethod = method?.MakeGenericMethod(genericType);
        var stateFromJson = genericMethod?.Invoke(typeof(SekibanJsonHelper), new object?[] { json });

        //var stateFromJson = SekibanJsonHelper.Deserialize<AggregateState<TAggregatePayload>>(json);
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
        ThrowIfTestHasUnhandledErrors();
        _latestValidationErrors = new List<SekibanValidationParameterError>();
        _latestEvents = new List<IEvent>();
        _latestException = null;
    }

    #region Single Projection
    private SingleProjectionState<TSingleProjectionPayload> GetSingleProjectionState<TSingleProjectionPayload>()
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
    {
        var singleProjection = _serviceProvider.GetService<IAggregateLoader>() ??
            throw new Exception("Failed to get single projection service");
        return singleProjection.AsSingleProjectionStateAsync<TSingleProjectionPayload>(GetAggregateId()).Result ??
            throw new Exception(
                "Failed to get single projection state for " +
                typeof(TSingleProjectionPayload).Name +
                " and " +
                GetAggregateId());
    }

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionStateIs<TSingleProjectionPayload>(
        SingleProjectionState<TSingleProjectionPayload> state)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
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

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionPayloadIs<TSingleProjectionPayload>(
        TSingleProjectionPayload payload)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
    {
        var actual = GetSingleProjectionState<TSingleProjectionPayload>().Payload;
        var expected = payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleProjectionPayload<TSingleProjectionPayload>(
        Action<TSingleProjectionPayload> payloadAction) where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
    {
        payloadAction(GetSingleProjectionState<TSingleProjectionPayload>().Payload);
        return this;
    }

    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleProjectionState<TSingleProjectionPayload>(
        Action<SingleProjectionState<TSingleProjectionPayload>> stateAction)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
    {
        stateAction(GetSingleProjectionState<TSingleProjectionPayload>());
        return this;
    }

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionPayloadIsFromJson<TSingleProjectionPayload>(
        string payloadJson)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
    {
        var actual = GetSingleProjectionState<TSingleProjectionPayload>().Payload;
        var payload = JsonSerializer.Deserialize<TSingleProjectionPayload>(payloadJson);
        if (payload is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        var expected = payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionPayloadIsFromFile<TSingleProjectionPayload>(
        string payloadFilename)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
    {
        using var openStream = File.OpenRead(payloadFilename);
        var actual = GetSingleProjectionState<TSingleProjectionPayload>().Payload;
        var payload = JsonSerializer.Deserialize<TSingleProjectionPayload>(openStream);
        if (payload is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        var expected = payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public IAggregateTestHelper<TAggregatePayload> WriteSingleProjectionStateToFile<TSingleProjectionPayload>(
        string filename)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
    {
        var state = GetSingleProjectionState<TSingleProjectionPayload>();
        var json = SekibanJsonHelper.Serialize(state);
        File.WriteAllText(filename, json);
        return this;
    }
    #endregion



    #region General List Query Test
    private ListQueryResult<TQueryResponse> GetListQueryResponse<TQueryResponse>(
        IListQueryInput<TQueryResponse> param)
        where TQueryResponse : IQueryResponse
    {
        var queryService = _serviceProvider.GetService<IQueryExecutor>() ??
            throw new Exception("Failed to get Query service");
        return queryService.ExecuteAsync(param)
                .Result ??
            throw new Exception("Failed to get Aggregate Query Response for " + param.GetType().Name);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIs<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        ListQueryResult<TQueryResponse> expectedResponse)
        where TQueryResponse : IQueryResponse
    {
        var actual = GetListQueryResponse(param);
        var expected = expectedResponse;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> WriteQueryResponseToFile<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        string filename)
        where TQueryResponse : IQueryResponse
    {
        var json = SekibanJsonHelper.Serialize(GetListQueryResponse(param));
        if (string.IsNullOrEmpty(json))
        {
            throw new InvalidDataException("Json is null or empty");
        }
        File.WriteAllTextAsync(filename, json);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenGetQueryResponse<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        Action<ListQueryResult<TQueryResponse>> responseAction)
        where TQueryResponse : IQueryResponse
    {
        responseAction(GetListQueryResponse(param)!);
        return this;
    }

    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIsFromJson<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        string responseJson)
        where TQueryResponse : IQueryResponse
    {
        var response = JsonSerializer.Deserialize<ListQueryResult<TQueryResponse>>(responseJson);
        if (response is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        ThenQueryResponseIs(param, response);
        return this;
    }

    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIsFromFile<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        string responseFilename)
        where TQueryResponse : IQueryResponse
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<ListQueryResult<TQueryResponse>>(openStream);
        if (response is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        ThenQueryResponseIs(param, response);
        return this;
    }
    #endregion

    #region Query Test (not list)
    private TQueryResponse GetQueryResponse<TQueryResponse>(
        IQueryInput<TQueryResponse> param)
        where TQueryResponse : IQueryResponse
    {
        var queryService = _serviceProvider.GetService<IQueryExecutor>() ??
            throw new Exception("Failed to get Query service");
        return queryService.ExecuteAsync(param)
                .Result ??
            throw new Exception("Failed to get Aggregate Query Response for " + param.GetType().Name);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIs<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        TQueryResponse expectedResponse)
        where TQueryResponse : IQueryResponse
    {
        var actual = GetQueryResponse(param);
        var expected = expectedResponse;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> WriteQueryResponseToFile<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        string filename)
        where TQueryResponse : IQueryResponse
    {
        var json = SekibanJsonHelper.Serialize(GetQueryResponse(param));
        if (string.IsNullOrEmpty(json))
        {
            throw new InvalidDataException("Json is null or empty");
        }
        File.WriteAllTextAsync(filename, json);
        return this;
    }
    public IAggregateTestHelper<TAggregatePayload> ThenGetQueryResponse<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        Action<TQueryResponse> responseAction)
        where TQueryResponse : IQueryResponse
    {
        responseAction(GetQueryResponse(param)!);
        return this;
    }

    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIsFromJson<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        string responseJson)
        where TQueryResponse : IQueryResponse
    {
        var response = JsonSerializer.Deserialize<TQueryResponse>(responseJson);
        if (response is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        ThenQueryResponseIs(param, response);
        return this;
    }

    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIsFromFile<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        string responseFilename)
        where TQueryResponse : IQueryResponse
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<TQueryResponse>(openStream);
        if (response is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        ThenQueryResponseIs(param, response);
        return this;
    }
    #endregion
}
