using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
namespace Sekiban.Pure.Repositories;

public class Repository
{
    private readonly List<IEvent> _events = new();
    private readonly object _lock = new();

    public IReadOnlyList<IEvent> Events
    {
        get
        {
            lock (_lock)
            {
                return _events.ToList();
            }
        }
    }

    public Func<string, IEvent> Deserializer { get; set; } = s => throw new NotImplementedException();
    public Func<IEvent, string> Serializer { get; set; } = s => throw new NotImplementedException();

    public ResultBox<Aggregate> Load<TAggregateProjector>(PartitionKeys partitionKeys)
        where TAggregateProjector : IAggregateProjector, new() =>
        Load(partitionKeys, new TAggregateProjector());

    public ResultBox<Aggregate> Load(PartitionKeys partitionKeys, IAggregateProjector projector)
    {
        List<IEvent> events;
        lock (_lock)
        {
            events = _events
                .Where(e => e.PartitionKeys.Equals(partitionKeys))
                .OrderBy(e => e.SortableUniqueId)
                .ToList();
        }
        return ResultBox
            .FromValue(events)
            .Conveyor(evts => Aggregate.EmptyFromPartitionKeys(partitionKeys).Project(evts, projector));
    }

    public ResultBox<List<IEvent>> Save(List<IEvent> events)
    {
        lock (_lock)
        {
            _events.AddRange(events);
        }
        return ResultBox.FromValue(events);
    }

    /// <summary>
    ///     Clears all events from the repository
    /// </summary>
    /// <returns>A ResultBox containing the number of events that were removed</returns>
    public ResultBox<int> ClearAllEvents()
    {
        lock (_lock)
        {
            var count = _events.Count;
            _events.Clear();
            return ResultBox.FromValue(count);
        }
    }

    public Task<ResultBox<MultiProjectionState<TMultiProjection>>> LoadMultiProjection<TMultiProjection>(
        IMultiProjectionEventSelector eventSelector) where TMultiProjection : IMultiProjector<TMultiProjection>
    {
        List<IEvent> events;
        lock (_lock)
        {
            events = _events.Where(eventSelector.GetEventSelector).OrderBy(e => e.SortableUniqueId).ToList();
        }
        return ResultBox
            .FromValue(events)
            .Conveyor(
                evts => evts
                    .ToResultBox()
                    .ReduceEach(new MultiProjectionState<TMultiProjection>(), (ev, state) => state.ApplyEvent(ev)))
            .ToTask();
    }
}
