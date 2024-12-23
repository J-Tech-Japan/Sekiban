using Sekiban.Core.Documents.ValueObjects;
namespace Sekiban.Core.Query.SingleProjections.Projections;

public interface IMemoryCacheContainer
{
    public SortableUniqueIdValue? SafeSortableUniqueId { get; }
}
