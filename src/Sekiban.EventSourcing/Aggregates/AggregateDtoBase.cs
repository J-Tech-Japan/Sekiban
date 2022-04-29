using Sekiban.EventSourcing.Queries;
namespace Sekiban.EventSourcing.Aggregates;

public abstract record AggregateDtoBase : ISingleAggregate
{
    public bool IsDeleted { get; init; }
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
        IsDeleted = aggregate.IsDeleted;
    }
    public Guid AggregateId { get; init; }
    public int Version { get; init; }
    public Guid LastEventId { get; init; }
}
