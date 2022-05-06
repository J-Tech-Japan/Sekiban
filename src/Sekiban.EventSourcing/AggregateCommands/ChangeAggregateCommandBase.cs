namespace Sekiban.EventSourcing.AggregateCommands;

public abstract record ChangeAggregateCommandBase<T> : IAggregateCommand where T : IAggregate
{
    // WebApiに公開しないしないようinternalにする
    internal Guid AggregateId { get; init; }

    public int ReferenceVersion { get; init; }

    public ChangeAggregateCommandBase(Guid aggregateId) =>
        AggregateId = aggregateId;
}
