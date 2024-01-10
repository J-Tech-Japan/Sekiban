using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
namespace Sekiban.Infrastructure.Cosmos;

public interface ICosmosDbFactory
{
    Task<T> CosmosActionAsync<T>(DocumentType documentType, AggregateContainerGroup containerGroup, Func<Container, Task<T>> cosmosAction);

    Task CosmosActionAsync(DocumentType documentType, AggregateContainerGroup containerGroup, Func<Container, Task> cosmosAction);

    Task DeleteAllFromEventContainer(AggregateContainerGroup containerGroup);
    Task DeleteAllFromItemsContainer(AggregateContainerGroup containerGroup);
}
