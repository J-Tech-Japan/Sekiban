using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Documents;

public class InMemoryDocumentRemover : IDocumentRemover
{
    private readonly InMemoryDocumentStore _inMemoryDocumentStore;
    public InMemoryDocumentRemover(InMemoryDocumentStore inMemoryDocumentStore)
    {
        _inMemoryDocumentStore = inMemoryDocumentStore;
    }

    public Task RemoveAllEventsAsync(AggregateContainerGroup aggregateContainerGroup)
    {
        _inMemoryDocumentStore.ResetInMemoryStore();
        return Task.CompletedTask;
    }
    public Task RemoveAllItemsAsync(AggregateContainerGroup aggregateContainerGroup)
    {
        return Task.CompletedTask;
    }
}