using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.SingleAggregates;
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
    public IAggregateTestHelper<TAggregate, TContents> Given<TEventPayload>(TEventPayload payload) where TEventPayload : IChangedEventPayload
    {
        var ev = AggregateEvent<TEventPayload>.ChangedEvent(_aggregate.AggregateId, payload, typeof(TAggregate));
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

    public IAggregateTestHelper<TAggregate, TContents> WhenCreate<C>(Guid aggregateId, C createCommand) where C : ICreateAggregateCommand<TAggregate>
    {
        ResetBeforeCommand();
        var handler
            = _serviceProvider.GetService(typeof(ICreateAggregateCommandHandler<TAggregate, C>)) as ICreateAggregateCommandHandler<TAggregate, C>;
        if (handler == null)
        {
            throw new SekibanAggregateCommandNotRegisteredException(typeof(C).Name);
        }
        var commandDocument = new AggregateCommandDocument<C>(createCommand, new CanNotUsePartitionKeyFactory());
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

    public IAggregateTestHelper<TAggregate, TContents> WhenChange<C>(C createCommand) where C : ChangeAggregateCommandBase<TAggregate>
    {
        ResetBeforeCommand();
        var handler
            = _serviceProvider.GetService(typeof(IChangeAggregateCommandHandler<TAggregate, C>)) as IChangeAggregateCommandHandler<TAggregate, C>;
        if (handler == null)
        {
            throw new SekibanAggregateCommandNotRegisteredException(typeof(C).Name);
        }
        var commandDocument = new AggregateCommandDocument<C>(createCommand, new CanNotUsePartitionKeyFactory());
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
        if (handler == null)
        {
            throw new SekibanAggregateCommandNotRegisteredException(typeof(C).Name);
        }
        var command = commandFunc(_aggregate);
        var commandDocument = new AggregateCommandDocument<C>(command, new CanNotUsePartitionKeyFactory());
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
    public IAggregateTestHelper<TAggregate, TContents> WhenConstructor(Func<TAggregate> aggregateFunc)
    {
        ResetBeforeCommand();
        try
        {
            _aggregate = aggregateFunc();
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
        Assert.Equal(_latestEvents.First().GetPayload(), payload);
        return this;
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
        Assert.Equal((T)actual, expected);
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenState(Func<TAggregate, AggregateDto<TContents>> constructExpectedDto)
    {
        var actual = _aggregate.ToDto();
        var expected = constructExpectedDto(_aggregate).GetComparableObject(actual);
        Assert.Equal(actual, (AggregateDto<TContents>)expected);
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
        var ev = AggregateEvent<TEventPayload>.CreatedEvent(aggregateId, payload, typeof(TAggregate));
        if (_aggregate.CanApplyEvent(ev)) { _aggregate.ApplyEvent(ev); }
        return this;
    }
    public IAggregateTestHelper<TAggregate, TContents> Given(IChangedEventPayload payload) =>
        throw new NotImplementedException();

    private void CheckStateJSONSupports()
    {
        var dto = _aggregate.ToDto();
        var fromDto = _projector.CreateInitialAggregate(dto.AggregateId);
        fromDto.ApplySnapshot(dto);
        var dtoFromSnapshot = fromDto.ToDto().GetComparableObject(dto);
        Assert.Equal(dto, dtoFromSnapshot);
        var json = Shared.SekibanJsonHelper.Serialize(dto);
        var dtoFromJson = Shared.SekibanJsonHelper.Deserialize<AggregateDto<TContents>>(json);
        Assert.Equal(dto, dtoFromJson);
        CheckEventJsonCompatibility();
    }

    private void CheckEventJsonCompatibility()
    {
        foreach (var ev in _latestEvents)
        {
            var type = ev.GetType();
            var json = Shared.SekibanJsonHelper.Serialize(ev);
            var eventFromJson = Shared.SekibanJsonHelper.Deserialize(json, type);
            var json2 = Shared.SekibanJsonHelper.Serialize(eventFromJson);
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
