using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.SingleAggregates;
using Xunit;
namespace Sekiban.EventSourcing.TestHelpers;

public class AggregateTestHelper<TAggregate, TDto> : IAggregateTestHelper<TAggregate, TDto>
    where TAggregate : TransferableAggregateBase<TDto> where TDto : AggregateDtoBase
{
    private readonly IServiceProvider _serviceProvider;
    private TAggregate Aggregate { get; set; }

    public List<AggregateEvent> LatestEvents { get; set; } = new();
    private DefaultSingleAggregateProjector<TAggregate> _projector
    {
        get;
    }

    public AggregateTestHelper(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _projector = new DefaultSingleAggregateProjector<TAggregate>();
        Aggregate = _projector.CreateInitialAggregate(Guid.Empty);
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
    public AggregateTestHelper<TAggregate, TDto> Given(TDto snapshot)
    {
        Aggregate.ApplySnapshot(snapshot);
        return this;
    }
    public AggregateTestHelper<TAggregate, TDto> Given(AggregateEvent ev)
    {
        if (Aggregate.CanApplyEvent(ev)) { Aggregate.ApplyEvent(ev); }
        return this;
    }
    public AggregateTestHelper<TAggregate, TDto> Given(Func<TAggregate, AggregateEvent> evFunc)
    {
        var ev = evFunc(Aggregate);
        if (Aggregate.CanApplyEvent(ev)) { Aggregate.ApplyEvent(ev); }
        return this;
    }
    public AggregateTestHelper<TAggregate, TDto> Given(IEnumerable<AggregateEvent> events)
    {
        foreach (var ev in events)
        {
            if (Aggregate.CanApplyEvent(ev)) { Aggregate.ApplyEvent(ev); }
        }
        return this;
    }
    public AggregateTestHelper<TAggregate, TDto> Given(TDto snapshot, AggregateEvent ev) =>
        Given(snapshot).Given(ev);
    public AggregateTestHelper<TAggregate, TDto> Given(TDto snapshot, IEnumerable<AggregateEvent> ev) =>
        Given(snapshot).Given(ev);

    public AggregateTestHelper<TAggregate, TDto> WhenCreate<C>(C createCommand) where C : ICreateAggregateCommand<TAggregate>
    {
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
            Aggregate = result.Aggregate;
        }
        catch (AggregateException ex)
        {
            throw ex.InnerExceptions.First();
        }
        LatestEvents = Aggregate.Events.ToList();
        Aggregate.ResetEventsAndSnapshots();
        return this;
    }

    public AggregateTestHelper<TAggregate, TDto> WhenChange<C>(C createCommand) where C : ChangeAggregateCommandBase<TAggregate>
    {
        var handler
            = _serviceProvider.GetService(typeof(IChangeAggregateCommandHandler<TAggregate, C>)) as IChangeAggregateCommandHandler<TAggregate, C>;
        if (handler == null)
        {
            throw new SekibanAggregateCommandNotRegisteredException(typeof(C).Name);
        }
        var commandDocument = new AggregateCommandDocument<C>(createCommand, new CanNotUsePartitionKeyFactory());
        try
        {
            handler.HandleAsync(commandDocument, Aggregate).Wait();
        }
        catch (AggregateException ex)
        {
            throw ex.InnerExceptions.First();
        }
        LatestEvents = Aggregate.Events.ToList();
        Aggregate.ResetEventsAndSnapshots();
        return this;
    }
    public AggregateTestHelper<TAggregate, TDto> WhenChange<C>(Func<TAggregate, C> commandFunc) where C : ChangeAggregateCommandBase<TAggregate>
    {
        var handler
            = _serviceProvider.GetService(typeof(IChangeAggregateCommandHandler<TAggregate, C>)) as IChangeAggregateCommandHandler<TAggregate, C>;
        if (handler == null)
        {
            throw new SekibanAggregateCommandNotRegisteredException(typeof(C).Name);
        }
        var command = commandFunc(Aggregate);
        var commandDocument = new AggregateCommandDocument<C>(command, new CanNotUsePartitionKeyFactory());
        handler.HandleAsync(commandDocument, Aggregate).Wait();
        LatestEvents = Aggregate.Events.ToList();
        Aggregate.ResetEventsAndSnapshots();
        return this;
    }

    public AggregateTestHelper<TAggregate, TDto> WhenMethod(Action<TAggregate> action)
    {
        action(Aggregate);
        LatestEvents = Aggregate.Events.ToList();
        Aggregate.ResetEventsAndSnapshots();
        return this;
    }
    public AggregateTestHelper<TAggregate, TDto> WhenConstructor(TAggregate aggregate)
    {
        Aggregate = aggregate;
        LatestEvents = Aggregate.Events.ToList();
        Aggregate.ResetEventsAndSnapshots();
        return this;
    }

    public AggregateTestHelper<TAggregate, TDto> ThenEvents(Action<List<AggregateEvent>, TAggregate> checkEventsAction)
    {
        checkEventsAction(LatestEvents, Aggregate);
        return this;
    }
    public AggregateTestHelper<TAggregate, TDto> ThenEvents(Action<List<AggregateEvent>> checkEventsAction)
    {
        checkEventsAction(LatestEvents);
        return this;
    }
    public AggregateTestHelper<TAggregate, TDto> ThenSingleEvent(Action<AggregateEvent, TAggregate> checkEventAction)
    {
        if (LatestEvents.Count != 1) { throw new SekibanInvalidArgumentException(); }
        checkEventAction(LatestEvents.First(), Aggregate);
        return this;
    }

    public AggregateTestHelper<TAggregate, TDto> ThenSingleEvent(Action<AggregateEvent> checkEventAction)
    {
        if (LatestEvents.Count != 1) { throw new SekibanInvalidArgumentException(); }
        checkEventAction(LatestEvents.First());
        return this;
    }
    public AggregateTestHelper<TAggregate, TDto> Expect(Action<TDto, TAggregate> checkDtoAction)
    {
        checkDtoAction(Aggregate.ToDto(), Aggregate);
        return this;
    }
    public AggregateTestHelper<TAggregate, TDto> Expect(Action<TDto> checkDtoAction)
    {
        checkDtoAction(Aggregate.ToDto());
        return this;
    }
    public AggregateTestHelper<TAggregate, TDto> ThenSingleEvent<T>(Action<T, TAggregate> checkEventAction) where T : AggregateEvent
    {
        if (LatestEvents.Count != 1) { throw new SekibanInvalidArgumentException(); }
        Assert.IsType<T>(LatestEvents.First());
        checkEventAction((T)LatestEvents.First(), Aggregate);
        return this;
    }
    public AggregateTestHelper<TAggregate, TDto> ThenSingleEvent<T>(Action<T> checkEventAction) where T : AggregateEvent
    {
        if (LatestEvents.Count != 1) { throw new SekibanInvalidArgumentException(); }
        Assert.IsType<T>(LatestEvents.First());
        checkEventAction((T)LatestEvents.First());
        return this;
    }
}
