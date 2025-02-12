using System.Reflection;
using Microsoft.Azure.Cosmos;
using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.OrleansEventSourcing;

namespace Sekiban.Pure.CosmosDb;

public class CosmosDbEventWriter(CosmosDbFactory dbFactory, IEventTypes eventTypes) : IEventWriter
{
    
    public Task SaveEvents<TEvent>(IEnumerable<TEvent> events) where TEvent : IEvent=> dbFactory.CosmosActionAsync(
        DocumentType.Event,
        async container =>
        {
            var taskList = events.ToList()
                .Select(ev => eventTypes.ConvertToEventDocument(ev))
                .Select(ev => SaveEventFromEventDocument(ev.UnwrapBox(), container))
                .ToList();
            await Task.WhenAll(taskList);
        });

    public Task SaveEventFromEventDocument(IEventDocument eventDocument, Container container)
    {
        var documentType = eventDocument.GetType();
        var methods = container.GetType().GetMethods().Where(m => m.Name == nameof(Container.UpsertItemAsync));
        var method = methods.FirstOrDefault(m => m.Name == nameof(Container.UpsertItemAsync) && m.GetParameters().Length == 4);
        var genericMethod = method?.MakeGenericMethod(documentType);
        return ((Task?)genericMethod?.Invoke(container, new object?[] { eventDocument, CosmosPartitionGenerator.ForEvent(eventDocument) ,null, default(CancellationToken) })) ?? Task.CompletedTask;
    }
}