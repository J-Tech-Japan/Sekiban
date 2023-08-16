using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
namespace Sekiban.Infrastructure.Cosmos.Documents;

public class CosmosDbDocumentRemover : IDocumentRemover
{
    private readonly CosmosDbFactory _cosmosDbFactory;
    public CosmosDbDocumentRemover(CosmosDbFactory cosmosDbFactory) => _cosmosDbFactory = cosmosDbFactory;

    public async Task RemoveAllEventsAsync(AggregateContainerGroup aggregateContainerGroup)
    {
        await _cosmosDbFactory.DeleteAllFromEventContainer(aggregateContainerGroup);
    }
    public async Task RemoveAllItemsAsync(AggregateContainerGroup aggregateContainerGroup)
    {
        await _cosmosDbFactory.DeleteAllFromItemsContainer(aggregateContainerGroup);
    }
}
