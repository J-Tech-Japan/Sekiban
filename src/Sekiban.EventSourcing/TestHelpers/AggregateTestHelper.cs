using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.SingleAggregates;
using Sekiban.EventSourcing.Shared;
using Xunit;
namespace Sekiban.EventSourcing.TestHelpers;

public class AggregateTestHelper<TAggregate, TContents> : IAggregateTestHelper<TAggregate, TContents>
    where TAggregate : TransferableAggregateBase<TContents>, new() where TContents : IAggregateContents, new()
{
    private readonly IServiceProvider _serviceProvider;
    private TAggregate _aggregate { get; set; }
    private Exception? _latestException { get; set; }
    private List<IAggregateEvent> _latestEvents { get; set; } = new();
    private DefaultSingleAggregateProjector<TAggregate> _projector
    {
        get;
    }

    public AggregateTestHelper(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _projector = new DefaultSingleAggregateProjector<TAggregate>();
        _aggregate = _projector.CreateInitialAggregate(Guid.Empty);
    }

    public IAggregateTestHelper<TAggregate, TContents> GivenScenario(Action initialAction)
    {
        initialAction();
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> GivenEnvironmentDtos(List<ISingleAggregate> dtos)
    {
        var singleAggregateService = _serviceProvider.GetService<ISingleAggregateService>();
        var memorySingleAggregateService = singleAggregateService as MemorySingleAggregateService;
        memorySingleAggregateService?.Aggregates.AddRange(dtos);

        var multipleAggregateService = _serviceProvider.GetService<IMultipleAggregateProjectionService>() as MemoryMultipleAggregateProjectionService;
        multipleAggregateService?.Objects.AddRange(dtos);

        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> GivenEnvironmentDtoContents<DAggregate, DAggregateContents>(
        Guid aggregateId,
        DAggregateContents contents) where DAggregate : TransferableAggregateBase<DAggregateContents>, new()
        where DAggregateContents : IAggregateContents, new() =>
        GivenEnvironmentDto(new AggregateDto<DAggregateContents>(new DAggregate { AggregateId = aggregateId }, contents) { Version = 1 });
    public IAggregateTestHelper<TAggregate, TContents> Given(AggregateDto<TContents> snapshot)
    {
        _aggregate.ApplySnapshot(snapshot);
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> Given(IAggregateEvent ev)
    {
        if (_aggregate.CanApplyEvent(ev)) { _aggregate.ApplyEvent(ev); }
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> Given(Guid aggregateId, TContents contents)
    {
        Given(new AggregateDto<TContents>(new TAggregate { AggregateId = aggregateId }, contents) { Version = 1 });
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> Given<TEventPayload>(TEventPayload payload) where TEventPayload : IChangedEventPayload
    {
        var ev = AggregateEvent<TEventPayload>.ChangedEvent(_aggregate.AggregateId, typeof(TAggregate), payload);
        if (_aggregate.CanApplyEvent(ev)) { _aggregate.ApplyEvent(ev); }

        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> Given(Func<TAggregate, IAggregateEvent> evFunc)
    {
        var ev = evFunc(_aggregate);
        if (_aggregate.CanApplyEvent(ev)) { _aggregate.ApplyEvent(ev); }
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> Given(IEnumerable<IAggregateEvent> events)
    {
        foreach (var ev in events)
        {
            if (_aggregate.CanApplyEvent(ev)) { _aggregate.ApplyEvent(ev); }
        }
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> Given(AggregateDto<TContents> snapshot, IAggregateEvent ev) =>
        Given(snapshot).Given(ev);
    public IAggregateTestHelper<TAggregate, TContents> Given(AggregateDto<TContents> snapshot, IEnumerable<IAggregateEvent> ev) =>
        Given(snapshot).Given(ev);

    public IAggregateTestHelper<TAggregate, TContents> WhenCreate<C>(C createCommand) where C : ICreateAggregateCommand<TAggregate>
    {
        ResetBeforeCommand();
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
            var aggregate = new TAggregate { AggregateId = aggregateId };
            var result = handler.HandleAsync(commandDocument, aggregate).Result;
            _aggregate = result.Aggregate;
        }
        catch (Exception ex)
        {
            _latestException = ex;
            return this;
        }
        _latestEvents = _aggregate.Events.ToList();
        if (_latestEvents.Count == 0)
        {
            throw new SekibanCreateHasToMakeEventException();
        }
        _aggregate.ResetEventsAndSnapshots();
        CheckStateJSONSupports();
        return this;
    }

    public IAggregateTestHelper<TAggregate, TContents> WhenChange<C>(C changeCommand) where C : ChangeAggregateCommandBase<TAggregate>
    {
        ResetBeforeCommand();
        var handler
            = _serviceProvider.GetService(typeof(IChangeAggregateCommandHandler<TAggregate, C>)) as IChangeAggregateCommandHandler<TAggregate, C>;
        if (handler is null)
        {
            throw new SekibanAggregateCommandNotRegisteredException(typeof(C).Name);
        }
        var commandDocument = new AggregateCommandDocument<C>(_aggregate.AggregateId, changeCommand, typeof(TAggregate));
        try
        {
            handler.HandleAsync(commandDocument, _aggregate).Wait();
        }
        catch (Exception ex)
        {
            _latestException = ex;
            return this;
        }
        _latestEvents = _aggregate.Events.ToList();
        _aggregate.ResetEventsAndSnapshots();
        CheckStateJSONSupports();
        return this;
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
        var commandDocument = new AggregateCommandDocument<C>(_aggregate.AggregateId, command, typeof(TAggregate));
        try
        {
            handler.HandleAsync(commandDocument, _aggregate).Wait();
        }
        catch (Exception ex)
        {
            _latestException = ex;
            return this;
        }

        _latestEvents = _aggregate.Events.ToList();
        _aggregate.ResetEventsAndSnapshots();
        CheckStateJSONSupports();
        return this;
    }

    public IAggregateTestHelper<TAggregate, TContents> WhenMethod(Action<TAggregate> action)
    {
        ResetBeforeCommand();
        try
        {
            action(_aggregate);
        }
        catch (Exception ex)
        {
            _latestException = ex;
            return this;
        }
        _latestEvents = _aggregate.Events.ToList();
        _aggregate.ResetEventsAndSnapshots();
        CheckStateJSONSupports();
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenEvents(Action<List<IAggregateEvent>, TAggregate> checkEventsAction)
    {
        checkEventsAction(_latestEvents, _aggregate);
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenEvents(Action<List<IAggregateEvent>> checkEventsAction)
    {
        checkEventsAction(_latestEvents);
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenSingleEventPayload<T>(T payload) where T : IEventPayload
    {
        if (_latestEvents.Count != 1) { throw new SekibanInvalidArgumentException(); }
        Assert.IsType<AggregateEvent<T>>(_latestEvents.First());

        var actualJson = SekibanJsonHelper.Serialize(_latestEvents.First().GetPayload());
        var expectedJson = SekibanJsonHelper.Serialize(payload);
        Assert.Equal(actualJson, expectedJson);
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenSingleEventPayload<T>(Func<TAggregate, T> constructExpectedEvent) where T : IEventPayload
    {
        var payload = constructExpectedEvent(_aggregate);
        return ThenSingleEventPayload(payload);
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenState(Action<AggregateDto<TContents>, TAggregate> checkDtoAction)
    {
        checkDtoAction(_aggregate.ToDto(), _aggregate);
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenState(Action<AggregateDto<TContents>> checkDtoAction)
    {
        checkDtoAction(_aggregate.ToDto());
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenSingleEvent<T>(Action<T, TAggregate> checkEventAction) where T : IAggregateEvent
    {
        if (_latestEvents.Count != 1) { throw new SekibanInvalidArgumentException(); }
        Assert.IsType<T>(_latestEvents.First());
        checkEventAction((T)_latestEvents.First(), _aggregate);
        return this;
    }

    public IAggregateTestHelper<TAggregate, TContents> ThenSingleEvent<T>(Action<T> checkEventAction) where T : IAggregateEvent
    {
        if (_latestEvents.Count != 1) { throw new SekibanInvalidArgumentException(); }
        Assert.IsType<T>(_latestEvents.First());
        checkEventAction((T)_latestEvents.First());
        return this;
    }

    public IAggregateTestHelper<TAggregate, TContents> ThenSingleEvent<T>(Func<TAggregate, T> constructExpectedEvent) where T : IAggregateEvent
    {
        if (_latestEvents.Count != 1) { throw new SekibanInvalidArgumentException(); }
        Assert.IsType<T>(_latestEvents.First());
        var actual = _latestEvents.First();
        var expected = constructExpectedEvent(_aggregate);
        expected = constructExpectedEvent(_aggregate).GetComparableObject(actual, expected.Version == 0);
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(actualJson, expectedJson);
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenState(Func<TAggregate, AggregateDto<TContents>> constructExpectedDto)
    {
        var actual = _aggregate.ToDto();
        var expected = constructExpectedDto(_aggregate).GetComparableObject(actual);
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(actualJson, expectedJson);
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenContents(TContents contents)
    {
        var actual = _aggregate.ToDto().Contents;
        var expected = contents;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(actualJson, expectedJson);
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenContents(Func<TAggregate, TContents> constructExpectedDto)
    {
        var actual = _aggregate.ToDto().Contents;
        var expected = constructExpectedDto(_aggregate);
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(actualJson, expectedJson);
        return this;
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
    public IAggregateTestHelper<TAggregate, TContents> ThenAggregateCheck(Action<TAggregate> checkAction)
    {
        checkAction(_aggregate);
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenNotThrowsAnException()
    {
        var exception = _latestException is AggregateException aggregateException ? aggregateException.InnerExceptions.First() : _latestException;
        Assert.Null(exception);
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> GivenEnvironmentDto(ISingleAggregate dto) =>
        GivenEnvironmentDtos(new List<ISingleAggregate> { dto });
    public IAggregateTestHelper<TAggregate, TContents> Given<TEventPayload>(Guid aggregateId, TEventPayload payload)
        where TEventPayload : ICreatedEventPayload
    {
        var ev = AggregateEvent<TEventPayload>.CreatedEvent(aggregateId, typeof(TAggregate), payload);
        if (_aggregate.CanApplyEvent(ev)) { _aggregate.ApplyEvent(ev); }
        return this;
    }

    private void CheckStateJSONSupports()
    {
        var dto = _aggregate.ToDto();
        var fromDto = _projector.CreateInitialAggregate(dto.AggregateId);
        fromDto.ApplySnapshot(dto);
        var dtoFromSnapshot = fromDto.ToDto().GetComparableObject(dto);
        var actualJson = SekibanJsonHelper.Serialize(dto);
        var expectedJson = SekibanJsonHelper.Serialize(dtoFromSnapshot);
        Assert.Equal(actualJson, expectedJson);
        var json = SekibanJsonHelper.Serialize(dto);
        var dtoFromJson = SekibanJsonHelper.Deserialize<AggregateDto<TContents>>(json);
        var dtoFromJsonJson = SekibanJsonHelper.Serialize(dtoFromJson);
        Assert.Equal(json, dtoFromJsonJson);
        CheckEventJsonCompatibility();
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
    public AggregateTestHelper<TAggregate, TContents> ThenSingleEvent(Action<IAggregateEvent, TAggregate> checkEventAction)
    {
        if (_latestEvents.Count != 1) { throw new SekibanInvalidArgumentException(); }
        checkEventAction(_latestEvents.First(), _aggregate);
        return this;
    }

    public AggregateTestHelper<TAggregate, TContents> ThenSingleEvent(Action<IAggregateEvent> checkEventAction)
    {
        if (_latestEvents.Count != 1) { throw new SekibanInvalidArgumentException(); }
        checkEventAction(_latestEvents.First());
        return this;
    }

    private void ResetBeforeCommand()
    {
        _latestEvents = new List<IAggregateEvent>();
        _latestException = null;
    }
}
