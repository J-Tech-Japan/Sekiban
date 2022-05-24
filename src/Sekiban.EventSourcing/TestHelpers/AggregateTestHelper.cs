using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.TestHelpers;

public class AggregateTestHelper<TAggregate, TDto> where TAggregate : TransferableAggregateBase<TDto> where TDto : AggregateDtoBase
{
    private readonly IServiceProvider _serviceProvider;
    public TAggregate Aggregate { get; set; }
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

    public async Task<AggregateTestHelper<TAggregate, TDto>> WhenCreate<C>(C createCommand) where C : ICreateAggregateCommand<TAggregate>
    {
        var handler
            = _serviceProvider.GetService(typeof(ICreateAggregateCommandHandler<TAggregate, C>)) as ICreateAggregateCommandHandler<TAggregate, C>;
        if (handler == null)
        {
            throw new JJAggregateCommandNotRegisteredException(typeof(C).Name);
        }
        var commandDocument = new AggregateCommandDocument<C>(createCommand, new CanNotUsePartitionKeyFactory());
        var result = await handler.HandleAsync(commandDocument);
        Aggregate = result.Aggregate;
        LatestEvents = Aggregate.Events.ToList();
        Aggregate.ResetEventsAndSnapshots();
        return this;
    }

    public async Task<AggregateTestHelper<TAggregate, TDto>> WhenChange<C>(C createCommand) where C : ChangeAggregateCommandBase<TAggregate>
    {
        var handler
            = _serviceProvider.GetService(typeof(IChangeAggregateCommandHandler<TAggregate, C>)) as IChangeAggregateCommandHandler<TAggregate, C>;
        if (handler == null)
        {
            throw new JJAggregateCommandNotRegisteredException(typeof(C).Name);
        }
        var commandDocument = new AggregateCommandDocument<C>(createCommand, new CanNotUsePartitionKeyFactory());
        await handler.HandleAsync(commandDocument, Aggregate);
        LatestEvents = Aggregate.Events.ToList();
        Aggregate.ResetEventsAndSnapshots();
        return this;
    }

    public AggregateTestHelper<TAggregate, TDto> Then(Action<List<AggregateEvent>> checkEventsAction)
    {
        checkEventsAction(LatestEvents);
        return this;
    }
    public AggregateTestHelper<TAggregate, TDto> Then(Action<AggregateEvent> checkEventAction)
    {
        if (LatestEvents.Count != 1) { throw new JJInvalidArgumentException(); }
        checkEventAction(LatestEvents.First());
        return this;
    }
    public AggregateTestHelper<TAggregate, TDto> Expect(Action<TDto> checkDtoAction)
    {
        checkDtoAction(Aggregate.ToDto());
        return this;
    }
}
