namespace Sekiban.Core.Aggregate;

public record AggregateVariable<TContents>(TContents Contents, bool IsDeleted = false) where TContents : IAggregateContents
{
}
