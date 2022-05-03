using System.Reflection;
namespace Sekiban.EventSourcing.Aggregates;

public abstract class AggregateBase : IAggregate
{
    protected readonly List<AggregateEvent> _events = new();
    protected static IPartitionKeyFactory DefaultPartitionKeyFactory =>
        new CanNotUsePartitionKeyFactory();

    public AggregateBase(Guid aggregateId) =>
        AggregateId = aggregateId;

    public Guid AggregateId { get; }
    public Guid LastEventId { get; protected set; } = Guid.Empty;
    public string LastSortableUniqueId { get; protected set; } = string.Empty;
    public int Version { get; protected set; }
    public bool IsDeleted { get; protected set; }

    public ReadOnlyCollection<AggregateEvent> Events => _events.AsReadOnly();

    public void ApplyEvent(AggregateEvent ev)
    {
        if (ev.IsAggregateInitialEvent == false && Version == 0)
        {
            throw new JJJnvalidEventException();
        }
        if (ev.Id == LastEventId) { return; }
        var action = GetApplyEventAction(ev);
        if (action == null) { return; }
        action();
        LastEventId = ev.Id;
        LastSortableUniqueId = ev.SortableUniqueId;
        Version++;
    }
    public void ResetEventsAndSnapshots()
    {
        _events.Clear();
    }

    public void FromEventHistory(IEnumerable<AggregateEvent> events)
    {
        foreach (var ev in events.OrderBy(m => m.TimeStamp))
        {
            ApplyEvent(ev);
        }
    }

    // TODO: 下記2行が必要か確認する
    public ISingleAggregateProjection CreateInitialAggregate(Guid _) => this;
    public ISingleAggregateProjection CreateInitialAggregate<T>(Guid _) => this;

    public static UAggregate Create<UAggregate>(Guid aggregateId)
        where UAggregate : AggregateBase
    {
        if (typeof(UAggregate).GetConstructor(new[] { typeof(Guid) }) is ConstructorInfo c)
        {
            return c.Invoke(new object[] { aggregateId }) as UAggregate ??
                throw new InvalidProgramException();
        }

        throw new InvalidProgramException();

        // C#の将来の正式バージョンで、インターフェースに静的メソッドを定義できるようになったら、書き換える。
    }

    protected abstract Action? GetApplyEventAction(AggregateEvent ev);

    protected abstract void AddAndApplyEvent(AggregateEvent ev);
}
