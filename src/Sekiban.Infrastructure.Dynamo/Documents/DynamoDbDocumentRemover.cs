using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
namespace Sekiban.Infrastructure.Dynamo.Documents;

public class DynamoDbDocumentRemover : IDocumentRemover
{
    private readonly DynamoDbFactory _dynamoDbFactory;
    public DynamoDbDocumentRemover(DynamoDbFactory dynamoDbFactory) => _dynamoDbFactory = dynamoDbFactory;

    public async Task RemoveAllEventsAsync(AggregateContainerGroup aggregateContainerGroup)
    {
        await _dynamoDbFactory.DeleteAllFromEventContainer(aggregateContainerGroup);
    }
    public async Task RemoveAllItemsAsync(AggregateContainerGroup aggregateContainerGroup)
    {
        await _dynamoDbFactory.DeleteAllFromItemsContainer(aggregateContainerGroup);
    }
}
