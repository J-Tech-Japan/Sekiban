using Microsoft.Azure.Cosmos;
using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
namespace Sekiban.Pure.CosmosDb;

public class CosmosDbEventWriter(CosmosDbFactory dbFactory, SekibanDomainTypes sekibanDomainTypes)
    : IEventWriter, IEventRemover
{

    public Task SaveEvents<TEvent>(IEnumerable<TEvent> events) where TEvent : IEvent => dbFactory.CosmosActionAsync(
        DocumentType.Event,
        async container =>
        {
            var taskList = events
                .ToList()
                .Select(ev => sekibanDomainTypes.EventTypes.ConvertToEventDocument(ev))
                .Select(ev => SaveEventFromEventDocument(ev.UnwrapBox(), container))
                .ToList();
            await Task.WhenAll(taskList);
        });

    public Task SaveEventFromEventDocument(IEventDocument eventDocument, Container container)
    {
        var documentType = eventDocument.GetType();
        var methods = container.GetType().GetMethods().Where(m => m.Name == nameof(Container.UpsertItemAsync));
        var method = methods.FirstOrDefault(
            m => m.Name == nameof(Container.UpsertItemAsync) && m.GetParameters().Length == 4);
        var genericMethod = method?.MakeGenericMethod(documentType);
        return (Task?)genericMethod?.Invoke(
                container,
                new object?[]
                {
                    eventDocument, CosmosPartitionGenerator.ForEvent(eventDocument), null, default(CancellationToken)
                }) ??
            Task.CompletedTask;
    }

    /// <summary>
    ///     Removes all events from the Cosmos DB event container
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public Task RemoveAllEvents() => dbFactory.DeleteAllFromEventContainer();
}
