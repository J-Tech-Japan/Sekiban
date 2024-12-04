using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Infrastructure.IndexedDb.Databases;

namespace Sekiban.Infrastructure.IndexedDb.Documents;

public class IndexedDbDocumentRemover(IndexedDbFactory dbFactory) : IDocumentRemover
{
    public async Task RemoveAllEventsAsync(AggregateContainerGroup aggregateContainerGroup)
    {
        await dbFactory.RemoveAllAsync(DocumentType.Event, aggregateContainerGroup);
    }

    public async Task RemoveAllItemsAsync(AggregateContainerGroup aggregateContainerGroup)
    {
        await dbFactory.RemoveAllAsync(DocumentType.Command, aggregateContainerGroup);
        await dbFactory.RemoveAllAsync(DocumentType.AggregateSnapshot, aggregateContainerGroup);
        await dbFactory.RemoveAllAsync(DocumentType.MultiProjectionSnapshot, aggregateContainerGroup);
    }
}
