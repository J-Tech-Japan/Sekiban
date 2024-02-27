using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
namespace Sekiban.Infrastructure.Postgres.Documents;

public class PostgresDbDocumentRemover(PostgresDbFactory dbFactory) : IDocumentRemover
{

    public async Task RemoveAllEventsAsync(AggregateContainerGroup aggregateContainerGroup)
    {
        await dbFactory.DeleteAllFromEventContainer(aggregateContainerGroup);
    }
    public async Task RemoveAllItemsAsync(AggregateContainerGroup aggregateContainerGroup)
    {
        await dbFactory.DeleteAllFromItemsContainer(aggregateContainerGroup);
    }
}
