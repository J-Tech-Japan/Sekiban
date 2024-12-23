using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Documents;

/// <summary>
///     In Memory Document Remover
///     Developer does not need to use this class
///     Use interface <see cref="IDocumentRemover" />
/// </summary>
public class InMemoryDocumentRemover : IDocumentRemover
{
    private readonly InMemoryDocumentStore _inMemoryDocumentStore;
    public InMemoryDocumentRemover(InMemoryDocumentStore inMemoryDocumentStore) =>
        _inMemoryDocumentStore = inMemoryDocumentStore;

    public Task RemoveAllEventsAsync(AggregateContainerGroup aggregateContainerGroup)
    {
        _inMemoryDocumentStore.ResetInMemoryStore();
        return Task.CompletedTask;
    }
    public Task RemoveAllItemsAsync(AggregateContainerGroup aggregateContainerGroup) => Task.CompletedTask;
}
