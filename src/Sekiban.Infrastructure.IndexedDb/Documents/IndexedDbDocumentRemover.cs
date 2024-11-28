using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;

namespace Sekiban.Infrastructure.IndexedDb.Documents;

public class IndexedDbDocumentRemover : IDocumentRemover
{
    public Task RemoveAllEventsAsync(AggregateContainerGroup aggregateContainerGroup)
    {
        throw new NotImplementedException();
    }

    public Task RemoveAllItemsAsync(AggregateContainerGroup aggregateContainerGroup)
    {
        throw new NotImplementedException();
    }
}
