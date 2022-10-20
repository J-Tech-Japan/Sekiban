using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Document;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.SingleAggregate;
using Sekiban.Core.Shared;
using Sekiban.Core.Validation;
using Sekiban.Testing.Command;
using System.Text.Json;
using Xunit;
namespace Sekiban.Testing.SingleAggregate;

public class AggregateTestHelper<TAggregate, TContents> : IAggregateTestHelper<TAggregate, TContents>
    where TAggregate : AggregateBase<TContents>, new() where TContents : IAggregateContents, new()
{
    private readonly AggregateTestCommandExecutor _commandExecutor;
    private readonly IServiceProvider _serviceProvider;
    private TAggregate _aggregate
    {
        get;
        set;
    }
    private Exception? _latestException { get; set; }
    private List<IAggregateEvent> _latestEvents { get; set; } = new();
    private List<SekibanValidationParameterError> _latestValidationErrors { get; set; } = new();

    private DefaultSingleAggregateProjector<TAggregate> _projector
    {
        get;
    }

    private List<SingleAggregateTestBase> SingleAggregateProjections
    {
        get;
    } = new();

    public AggregateTestHelper(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _projector = new DefaultSingleAggregateProjector<TAggregate>();
        _aggregate = _projector.CreateInitialAggregate(Guid.Empty);
        _commandExecutor = new AggregateTestCommandExecutor(_serviceProvider);
    }
    public TSingleAggregateProjection SetupSingleAggregateProjection<TSingleAggregateProjection>()
        where TSingleAggregateProjection : SingleAggregateTestBase
    {
        var singleAggregateProjection = Activator.CreateInstance(typeof(TSingleAggregateProjection), _serviceProvider) as TSingleAggregateProjection;
        if (singleAggregateProjection == null) { throw new Exception("Could not create single aggregate projection"); }
        SingleAggregateProjections.Add(singleAggregateProjection);
        return singleAggregateProjection;
    }
    public IAggregateTestHelper<TAggregate, TContents> GivenScenario(Action initialAction)
    {
        initialAction();
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> GivenEnvironmentEvent(IAggregateEvent ev)
    {
        var documentWriter = _serviceProvider.GetRequiredService(typeof(IDocumentWriter)) as IDocumentWriter;
        if (documentWriter is null) { throw new Exception("Failed to get document writer"); }
        documentWriter.SaveAsync(ev, typeof(TAggregate)).Wait();
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> GivenEnvironmentEvents(IEnumerable<IAggregateEvent> events)
    {
        foreach (var ev in events)
        {
            GivenEnvironmentEvent(ev);
        }
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> GivenEnvironmentEventsFile(string filename)
    {
        using var openStream = File.OpenRead(filename);
        var list = JsonSerializer.Deserialize<List<JsonElement>>(openStream);
        if (list is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        AddEventsFromList(list);
        return this;
    }
    public AggregateDto<TEnvironmentAggregateContents>
        GetEnvironmentAggregateDto<TEnvironmentAggregate, TEnvironmentAggregateContents>(Guid aggregateId)
        where TEnvironmentAggregate : AggregateBase<TEnvironmentAggregateContents>, new()
        where TEnvironmentAggregateContents : IAggregateContents, new()
    {
        var singleAggregateService = _serviceProvider.GetRequiredService(typeof(ISingleAggregateService)) as ISingleAggregateService;
        if (singleAggregateService is null) { throw new Exception("Failed to get single aggregate service"); }
        var aggregate = singleAggregateService.GetAggregateDtoAsync<TEnvironmentAggregate, TEnvironmentAggregateContents>(aggregateId).Result;
        return aggregate ?? throw new SekibanAggregateNotExistsException(aggregateId, typeof(TEnvironmentAggregate).Name);
    }
    public IReadOnlyCollection<IAggregateEvent> GetLatestEnvironmentEvents()
    {
        return _commandExecutor.LatestEvents;
    }

    public IAggregateTestHelper<TAggregate, TContents> WhenCreate<C>(C createCommand) where C : ICreateAggregateCommand<TAggregate>
    {
        ResetBeforeCommand();

        var validationResults = createCommand.TryValidateProperties().ToList();
        if (validationResults.Any())
        {
            _latestValidationErrors = SekibanValidationParameterError.CreateFromValidationResults(validationResults).ToList();
            return this;
        }
        var handler
            = _serviceProvider.GetService(typeof(ICreateAggregateCommandHandler<TAggregate, C>)) as ICreateAggregateCommandHandler<TAggregate, C>;
        if (handler is null)
        {
            throw new SekibanAggregateCommandNotRegisteredException(typeof(C).Name);
        }
        var aggregateId = handler.GenerateAggregateId(createCommand);
        var commandDocument = new AggregateCommandDocument<C>(aggregateId, createCommand, typeof(TAggregate));
        try
        {
            _aggregate = new TAggregate { AggregateId = aggregateId };
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
        SingleAggregateProjections.ForEach(m => m.SetAggregateId(_aggregate.AggregateId));
        SaveEvents(_latestEvents);
        CheckCommandJSONSupports(commandDocument);
        _aggregate.ResetEventsAndSnapshots();
        CheckStateJSONSupports();
        return this;
    }

    public IAggregateTestHelper<TAggregate, TContents> WhenChange<C>(C changeCommand) where C : ChangeAggregateCommandBase<TAggregate>
    {
        return WhenChange(_ => changeCommand);
    }
    public IAggregateTestHelper<TAggregate, TContents> WhenChange<C>(Func<TAggregate, C> commandFunc) where C : ChangeAggregateCommandBase<TAggregate>
    {
        ResetBeforeCommand();
        var handler
            = _serviceProvider.GetService(typeof(IChangeAggregateCommandHandler<TAggregate, C>)) as IChangeAggregateCommandHandler<TAggregate, C>;
        if (handler is null)
        {
            throw new SekibanAggregateCommandNotRegisteredException(typeof(C).Name);
        }
        var command = commandFunc(_aggregate);
        var validationResults = command.TryValidateProperties().ToList();
        if (validationResults.Any())
        {
            _latestValidationErrors = SekibanValidationParameterError.CreateFromValidationResults(validationResults).ToList();
            return this;
        }
        var commandDocument = new AggregateCommandDocument<C>(_aggregate.AggregateId, command, typeof(TAggregate));
        if (command is not IOnlyPublishingCommand)
        {
            try
            {
                handler.HandleAsync(commandDocument, _aggregate).Wait();
            }
            catch (Exception ex)
            {
                _latestException = ex;
                return this;
            }
            CheckCommandJSONSupports(commandDocument);
            _latestEvents = _aggregate.Events.ToList();
        } else
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
        _aggregate.ResetEventsAndSnapshots();
        CheckStateJSONSupports();
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenGetEvents(Action<List<IAggregateEvent>> checkEventsAction)
    {
        checkEventsAction(_latestEvents);
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenSingleEventIs<T>(AggregateEvent<T> aggregateEvent) where T : IEventPayload
    {
        if (_latestEvents.Count != 1) { throw new SekibanInvalidArgumentException(); }
        Assert.IsType<T>(_latestEvents.First());
        var actual = _latestEvents.First();
        var expected = aggregateEvent;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenSingleEventPayloadIs<T>(T payload) where T : IEventPayload
    {
        if (_latestEvents.Count != 1) { throw new SekibanInvalidArgumentException(); }
        Assert.IsType<AggregateEvent<T>>(_latestEvents.First());

        var actualJson = SekibanJsonHelper.Serialize(_latestEvents.First().GetPayload());
        var expectedJson = SekibanJsonHelper.Serialize(payload);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenGetSingleEventPayload<T>(Action<T> checkPayloadAction) where T : class, IEventPayload
    {
        if (_latestEvents.Count != 1) { throw new SekibanInvalidArgumentException(); }
        Assert.IsType<T>(_latestEvents.First().GetPayload());
        checkPayloadAction(_latestEvents.First().GetPayload() as T ?? throw new SekibanInvalidEventException());
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenGetState(Action<AggregateDto<TContents>> checkDtoAction)
    {
        checkDtoAction(_aggregate.ToDto());
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenStateIs(AggregateDto<TContents> expectedDto)
    {
        var actual = _aggregate.ToDto();
        var expected = expectedDto.GetComparableObject(actual);
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public IAggregateTestHelper<TAggregate, TContents> ThenGetSingleEvent<T>(Action<T> checkEventAction) where T : IAggregateEvent
    {
        if (_latestEvents.Count != 1) { throw new SekibanInvalidArgumentException(); }
        Assert.IsType<T>(_latestEvents.First());
        checkEventAction((T)_latestEvents.First());
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenContentsIs(TContents contents)
    {
        var actual = _aggregate.ToDto().Contents;
        var expected = contents;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> WriteStateToFile(string filename)
    {
        var actual = _aggregate.ToDto();
        var actualJson = SekibanJsonHelper.Serialize(actual);
        File.WriteAllText(filename, actualJson);
        return this;
    }
    public Guid GetAggregateId()
    {
        return _aggregate.AggregateId;
    }
    public int GetCurrentVersion()
    {
        return _aggregate.Version;
    }
    public TAggregate GetAggregate()
    {
        return _aggregate;
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenThrows<T>() where T : Exception
    {
        var exception = _latestException is AggregateException aggregateException ? aggregateException.InnerExceptions.First() : _latestException;
        Assert.IsType<T>(exception);
        return this;
    }

    public IAggregateTestHelper<TAggregate, TContents> ThenThrows<T>(Action<T> checkException) where T : Exception
    {
        var exception = _latestException is AggregateException aggregateException ? aggregateException.InnerExceptions.First() : _latestException;
        Assert.IsType<T>(exception);
        checkException((exception as T)!);
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenNotThrowsAnException()
    {
        var exception = _latestException is AggregateException aggregateException ? aggregateException.InnerExceptions.First() : _latestException;
        Assert.Null(exception);

        Assert.Empty(_latestValidationErrors);
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenHasValidationErrors(IEnumerable<SekibanValidationParameterError> validationParameterErrors)
    {
        var actual = _latestValidationErrors;
        var expected = validationParameterErrors;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenHasValidationErrors()
    {
        Assert.NotEmpty(_latestValidationErrors);
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> WriteContentsToFile(string filename)
    {
        var actual = _aggregate.ToDto().Contents;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        File.WriteAllText(filename, actualJson);
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenStateIsFromJson(string dtoJson)
    {
        var dto = JsonSerializer.Deserialize<AggregateDto<TContents>>(dtoJson);
        if (dto is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        var actual = _aggregate.ToDto();
        var expected = dto.GetComparableObject(actual);
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenStateIsFromFile(string dtoFileName)
    {
        using var openStream = File.OpenRead(dtoFileName);
        var dto = JsonSerializer.Deserialize<AggregateDto<TContents>>(openStream);
        if (dto is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        var actual = _aggregate.ToDto();
        var expected = dto.GetComparableObject(actual);
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public IAggregateTestHelper<TAggregate, TContents> ThenContentsIsFromJson(string contentsJson)
    {
        var contents = JsonSerializer.Deserialize<TContents>(contentsJson);
        if (contents is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        var actual = _aggregate.ToDto().Contents;
        var expected = contents;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public IAggregateTestHelper<TAggregate, TContents> ThenContentsIsFromFile(string contentsFileName)
    {
        using var openStream = File.OpenRead(contentsFileName);
        var contents = JsonSerializer.Deserialize<TContents>(openStream);
        if (contents is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        var actual = _aggregate.ToDto().Contents;
        var expected = contents;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public Guid RunEnvironmentCreateCommand<TEnvironmentAggregate>(
        ICreateAggregateCommand<TEnvironmentAggregate> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregate : AggregateCommonBase, new()
    {
        var (events, aggregateId) = _commandExecutor.ExecuteCreateCommand(command, injectingAggregateId);
        var aggregateEvents = events?.ToList() ?? new List<IAggregateEvent>();
        return aggregateId;
    }
    public void RunEnvironmentChangeCommand<TEnvironmentAggregate>(ChangeAggregateCommandBase<TEnvironmentAggregate> command)
        where TEnvironmentAggregate : AggregateCommonBase, new()
    {
        var _ = _commandExecutor.ExecuteChangeCommand(command);
    }
    public IAggregateTestHelper<TAggregate, TContents> GivenEnvironmentCommandExecutorAction(Action<AggregateTestCommandExecutor> action)
    {
        action(_commandExecutor);
        return this;
    }
    private void SaveEvents(IEnumerable<IAggregateEvent> events)
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
            var eventType = typeof(AggregateEvent<>).MakeGenericType(eventPayloadType);
            if (eventType is null)
            {
                throw new InvalidDataException($"イベント {documentTypeName} の生成に失敗しました。");
            }
            var eventInstance = JsonSerializer.Deserialize(json.ToString(), eventType);
            if (eventInstance is null)
            {
                throw new InvalidDataException($"イベント {documentTypeName} のデシリアライズに失敗しました。");
            }
            GivenEnvironmentEvent((AggregateEvent<IEventPayload>)eventInstance);
        }
    }

    private void CheckStateJSONSupports()
    {
        var dto = _aggregate.ToDto();
        var fromDto = _projector.CreateInitialAggregate(dto.AggregateId);
        fromDto.ApplySnapshot(dto);
        var dtoFromSnapshot = fromDto.ToDto().GetComparableObject(dto);
        var actualJson = SekibanJsonHelper.Serialize(dto);
        var expectedJson = SekibanJsonHelper.Serialize(dtoFromSnapshot);
        Assert.Equal(expectedJson, actualJson);
        var json = SekibanJsonHelper.Serialize(dto);
        var dtoFromJson = SekibanJsonHelper.Deserialize<AggregateDto<TContents>>(json);
        var dtoFromJsonJson = SekibanJsonHelper.Serialize(dtoFromJson);
        Assert.Equal(json, dtoFromJsonJson);
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
        _latestEvents = new List<IAggregateEvent>();
        _latestException = null;
    }
}
