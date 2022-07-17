using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.SingleAggregates;
using Xunit;
namespace Sekiban.EventSourcing.TestHelpers;

public class AggregateTestHelper<TAggregate, TDto> : IAggregateTestHelper<TAggregate, TDto>
    where TAggregate : TransferableAggregateBase<TDto> where TDto : AggregateDtoBase
{
    private readonly IServiceProvider _serviceProvider;
    private TAggregate _aggregate { get; set; }
    private Exception? _latestException { get; set; }
    private List<AggregateEvent> _latestEvents { get; set; } = new();
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

    public AggregateTestHelper<TAggregate, TDto> GivenScenario(Action initialAction)
    {
        initialAction();
        return this;
    }
    public AggregateTestHelper<TAggregate, TDto> GivenEnvironmentDtos(List<AggregateDtoBase> dtos)
    {
        var singleAggregateService = _serviceProvider.GetService<ISingleAggregateService>();
        var memorySingleAggregateService = singleAggregateService as MemorySingleAggregateService;
        memorySingleAggregateService?.Aggregates.AddRange(dtos);

        var multipleAggregateService = _serviceProvider.GetService<IMultipleAggregateProjectionService>() as MemoryMultipleAggregateProjectionService;
        multipleAggregateService?.Objects.AddRange(dtos);

        return this;
    }
    public AggregateTestHelper<TAggregate, TDto> GivenEnvironmentDto(AggregateDtoBase dto)
    {
        return GivenEnvironmentDtos(new List<AggregateDtoBase> { dto });
    }
    public AggregateTestHelper<TAggregate, TDto> Given(TDto snapshot)
    {
        _aggregate.ApplySnapshot(snapshot);
        return this;
    }
    public AggregateTestHelper<TAggregate, TDto> Given(AggregateEvent ev)
    {
        if (_aggregate.CanApplyEvent(ev)) { _aggregate.ApplyEvent(ev); }
        return this;
    }
    public AggregateTestHelper<TAggregate, TDto> Given(Func<TAggregate, AggregateEvent> evFunc)
    {
        var ev = evFunc(_aggregate);
        if (_aggregate.CanApplyEvent(ev)) { _aggregate.ApplyEvent(ev); }
        return this;
    }
    public AggregateTestHelper<TAggregate, TDto> Given(IEnumerable<AggregateEvent> events)
    {
        foreach (var ev in events)
        {
            if (_aggregate.CanApplyEvent(ev)) { _aggregate.ApplyEvent(ev); }
        }
        return this;
    }
    public AggregateTestHelper<TAggregate, TDto> Given(TDto snapshot, AggregateEvent ev)
    {
        return Given(snapshot).Given(ev);
    }
    public AggregateTestHelper<TAggregate, TDto> Given(TDto snapshot, IEnumerable<AggregateEvent> ev)
    {
        return Given(snapshot).Given(ev);
    }

    public AggregateTestHelper<TAggregate, TDto> WhenCreate<C>(C createCommand) where C : ICreateAggregateCommand<TAggregate>
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
            var result = handler.HandleAsync(commandDocument).Result;
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

    public AggregateTestHelper<TAggregate, TDto> WhenChange<C>(C createCommand) where C : ChangeAggregateCommandBase<TAggregate>
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
    public AggregateTestHelper<TAggregate, TDto> WhenChange<C>(Func<TAggregate, C> commandFunc) where C : ChangeAggregateCommandBase<TAggregate>
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

    public AggregateTestHelper<TAggregate, TDto> WhenMethod(Action<TAggregate> action)
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
    public AggregateTestHelper<TAggregate, TDto> WhenConstructor(Func<TAggregate> aggregateFunc)
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

    public AggregateTestHelper<TAggregate, TDto> ThenEvents(Action<List<AggregateEvent>, TAggregate> checkEventsAction)
    {
        checkEventsAction(_latestEvents, _aggregate);
        return this;
    }
    public AggregateTestHelper<TAggregate, TDto> ThenEvents(Action<List<AggregateEvent>> checkEventsAction)
    {
        checkEventsAction(_latestEvents);
        return this;
    }
    public AggregateTestHelper<TAggregate, TDto> ThenState(Action<TDto, TAggregate> checkDtoAction)
    {
        checkDtoAction(_aggregate.ToDto(), _aggregate);
        return this;
    }
    public AggregateTestHelper<TAggregate, TDto> ThenState(Action<TDto> checkDtoAction)
    {
        checkDtoAction(_aggregate.ToDto());
        return this;
    }
    public AggregateTestHelper<TAggregate, TDto> ThenSingleEvent<T>(Action<T, TAggregate> checkEventAction) where T : AggregateEvent
    {
        if (_latestEvents.Count != 1) { throw new SekibanInvalidArgumentException(); }
        Assert.IsType<T>(_latestEvents.First());
        checkEventAction((T)_latestEvents.First(), _aggregate);
        return this;
    }

    public AggregateTestHelper<TAggregate, TDto> ThenSingleEvent<T>(Action<T> checkEventAction) where T : AggregateEvent
    {
        if (_latestEvents.Count != 1) { throw new SekibanInvalidArgumentException(); }
        Assert.IsType<T>(_latestEvents.First());
        checkEventAction((T)_latestEvents.First());
        return this;
    }

    public AggregateTestHelper<TAggregate, TDto> ThenSingleEvent<T>(Func<TAggregate, T> constructExpectedEvent) where T : AggregateEvent
    {
        if (_latestEvents.Count != 1) { throw new SekibanInvalidArgumentException(); }
        Assert.IsType<T>(_latestEvents.First());
        var actual = _latestEvents.First();
        var expected = constructExpectedEvent(_aggregate);
        expected = constructExpectedEvent(_aggregate).GetComparableObject(actual, expected.Version == 0);
        Assert.Equal((T)actual, expected);
        return this;
    }
    public AggregateTestHelper<TAggregate, TDto> ThenState(Func<TAggregate, TDto> constructExpectedDto)
    {
        var actual = _aggregate.ToDto();
        var expected = constructExpectedDto(_aggregate).GetComparableObject(actual);
        Assert.Equal(actual, (TDto)expected);
        return this;
    }

    public AggregateTestHelper<TAggregate, TDto> ThenThrows<T>() where T : Exception
    {
        var exception = _latestException is AggregateException aggregateException ? aggregateException.InnerExceptions.First() : _latestException;
        Assert.IsType<T>(exception);
        return this;
    }
    public AggregateTestHelper<TAggregate, TDto> ThenThrows<T>(Action<T> checkException) where T : Exception
    {
        var exception = _latestException is AggregateException aggregateException ? aggregateException.InnerExceptions.First() : _latestException;
        Assert.IsType<T>(exception);
        checkException((exception as T)!);
        return this;
    }
    public AggregateTestHelper<TAggregate, TDto> ThenAggregateCheck(Action<TAggregate> checkAction)
    {
        checkAction(_aggregate);
        return this;
    }
    public AggregateTestHelper<TAggregate, TDto> ThenNotThrowsAnException()
    {
        var exception = _latestException is AggregateException aggregateException ? aggregateException.InnerExceptions.First() : _latestException;
        Assert.Null(exception);
        return this;
    }

    private void CheckStateJSONSupports()
    {
        var dto = _aggregate.ToDto();
        var fromDto = _projector.CreateInitialAggregate(dto.AggregateId);
        fromDto.ApplySnapshot(dto);
        var dtoFromSnapshot = fromDto.ToDto().GetComparableObject(dto);
        Assert.Equal(dto, dtoFromSnapshot);
        var json = JsonConvert.SerializeObject(dto);
        var dtoFromJson = JsonConvert.DeserializeObject<TDto>(json);
        Assert.Equal(dto, dtoFromJson);
        CheckEventJsonCompatibility();
    }

    private void CheckEventJsonCompatibility()
    {
        foreach (var ev in _latestEvents)
        {
            var type = ev.GetType();
            var json = JsonConvert.SerializeObject(ev);
            var eventFromJson = JsonConvert.DeserializeObject(json, type);
            var json2 = JsonConvert.SerializeObject(eventFromJson);
            Assert.Equal(json, json2);
        }
    }
    public AggregateTestHelper<TAggregate, TDto> ThenSingleEvent(Action<AggregateEvent, TAggregate> checkEventAction)
    {
        if (_latestEvents.Count != 1) { throw new SekibanInvalidArgumentException(); }
        checkEventAction(_latestEvents.First(), _aggregate);
        return this;
    }

    public AggregateTestHelper<TAggregate, TDto> ThenSingleEvent(Action<AggregateEvent> checkEventAction)
    {
        if (_latestEvents.Count != 1) { throw new SekibanInvalidArgumentException(); }
        checkEventAction(_latestEvents.First());
        return this;
    }

    private void ResetBeforeCommand()
    {
        _latestEvents = new List<AggregateEvent>();
        _latestException = null;
    }
}
