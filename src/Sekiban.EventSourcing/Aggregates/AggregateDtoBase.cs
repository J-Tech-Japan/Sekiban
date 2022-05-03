namespace Sekiban.EventSourcing.Aggregates;

public abstract record AggregateDtoBase : ISingleAggregate
{
    /// <summary>
    ///     スナップショットからの再構築用。
    /// </summary>
    public AggregateDtoBase() { }
    /// <summary>
    ///     一般の構築用。
    /// </summary>
    /// <param name="aggregate"></param>
    public AggregateDtoBase(IAggregate aggregate)
    {
        AggregateId = aggregate.AggregateId;
        Version = aggregate.Version;
        LastEventId = aggregate.LastEventId;
        LastSortableUniqueId = aggregate.LastSortableUniqueId;
        IsDeleted = aggregate.IsDeleted;
    }
    public bool IsDeleted { get; init; }
    public Guid AggregateId { get; init; }
    public int Version { get; init; }
    public Guid LastEventId { get; init; }
    public string LastSortableUniqueId { get; init; } = string.Empty;
}
