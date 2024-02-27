using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
namespace Sekiban.Infrastructure.Dynamo.Documents;

/// <summary>
///     Remove all documents from DynamoDB
///     Usually use for test.
/// </summary>
public class DynamoDbDocumentRemover(DynamoDbFactory dynamoDbFactory) : IDocumentRemover
{

    public async Task RemoveAllEventsAsync(AggregateContainerGroup aggregateContainerGroup)
    {
        await dynamoDbFactory.DeleteAllFromEventContainer(aggregateContainerGroup);
    }
    public async Task RemoveAllItemsAsync(AggregateContainerGroup aggregateContainerGroup)
    {
        await dynamoDbFactory.DeleteAllFromItemsContainer(aggregateContainerGroup);
    }
}
