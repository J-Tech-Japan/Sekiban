namespace Sekiban.Core.Aggregate;

public interface IDeletableAggregate
{
    public bool IsDeleted { get; }
}
