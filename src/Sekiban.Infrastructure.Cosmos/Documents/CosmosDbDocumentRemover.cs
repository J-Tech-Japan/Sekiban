using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
namespace Sekiban.Infrastructure.Cosmos.Documents;

/// <summary>
///     Remove all documents from CosmosDB
///     Usually only use in test
/// </summary>
public class CosmosDbDocumentRemover(CosmosDbFactory cosmosDbFactory) : IDocumentRemover
{

    public async Task RemoveAllEventsAsync(AggregateContainerGroup aggregateContainerGroup)
    {
        await cosmosDbFactory.DeleteAllFromEventContainer(aggregateContainerGroup);
    }
    public async Task RemoveAllItemsAsync(AggregateContainerGroup aggregateContainerGroup)
    {
        await cosmosDbFactory.DeleteAllFromItemsContainer(aggregateContainerGroup);
    }
}
