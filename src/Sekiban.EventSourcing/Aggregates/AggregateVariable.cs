namespace Sekiban.EventSourcing.Aggregates;

public record AggregateVariable<TContents>(TContents Contents, bool IsDeleted = false) where TContents : IAggregateContents
{
}
