using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Documents;

public interface IDocumentRemover
{
    public Task RemoveAllEventsAsync(AggregateContainerGroup aggregateContainerGroup);
    public Task RemoveAllItemsAsync(AggregateContainerGroup aggregateContainerGroup);
}
