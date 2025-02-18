using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
namespace Sekiban.Pure.Repositories;

public class Repository
{
    // 内部イベントリストは外部から直接変更されないよう隠蔽
    private readonly List<IEvent> _events = new();
    // 排他制御用のロックオブジェクト
    private readonly object _lock = new();

    // 外部からの読み取り用に、常に最新の状態のコピーを返す
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

    public Task<ResultBox<MultiProjectionState<TMultiProjection>>> LoadMultiProjection<TMultiProjection>(
        IMultiProjectionEventSelector eventSelector)
        where TMultiProjection : IMultiProjector<TMultiProjection>
    {
        List<IEvent> events;
        lock (_lock)
        {
            events = _events
                .Where(eventSelector.GetEventSelector)
                .OrderBy(e => e.SortableUniqueId)
                .ToList();
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