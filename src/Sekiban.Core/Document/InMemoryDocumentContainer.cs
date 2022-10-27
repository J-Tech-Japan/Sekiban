using Sekiban.Core.Event;
using System.Collections.Concurrent;
namespace Sekiban.Core.Document;

public class InMemoryDocumentStore
{
    private readonly ConcurrentDictionary<string, InMemoryDocumentContainer<IAggregateEvent>> _containerDictionary = new();
    public void ResetInMemoryStore()
    {
        _containerDictionary.Clear();
    }
    public void SaveEvent(IAggregateEvent document, string partition, string sekibanContextIdentifier)
    {
        if (!_containerDictionary.ContainsKey(sekibanContextIdentifier))
        {
            _containerDictionary[sekibanContextIdentifier] = new InMemoryDocumentContainer<IAggregateEvent>();
        }
        var eventContainer = _containerDictionary[sekibanContextIdentifier];
        eventContainer.All.Add(document);
        if (eventContainer.Partitions.ContainsKey(partition))
        {
            if (!eventContainer.Partitions[partition].Any(m => m.Id == document.Id))
            {
                eventContainer.Partitions[partition].Add(document);
            }
        }
        else
        {
            var partitionCollection = new BlockingCollection<IAggregateEvent>();
            partitionCollection.Add(document);
            eventContainer.Partitions[partition] = partitionCollection;
        }
        if (!eventContainer.All.Any(m => m.PartitionKey == document.PartitionKey && m.Id == document.Id))
        {
            eventContainer.All.Add(document);
        }
    }
    public IAggregateEvent[] GetAllEvents(string sekibanContextIdentifier)
    {
        if (!_containerDictionary.ContainsKey(sekibanContextIdentifier))
        {
            _containerDictionary[sekibanContextIdentifier] = new InMemoryDocumentContainer<IAggregateEvent>();
        }
        var eventContainer = _containerDictionary[sekibanContextIdentifier];
        return eventContainer.All.ToArray();
    }
    public IAggregateEvent[] GetEventPartition(string partition, string sekibanContextIdentifier)
    {
        if (!_containerDictionary.ContainsKey(sekibanContextIdentifier))
        {
            _containerDictionary[sekibanContextIdentifier] = new InMemoryDocumentContainer<IAggregateEvent>();
        }
        var eventContainer = _containerDictionary[sekibanContextIdentifier];
        if (eventContainer.Partitions.ContainsKey(partition))
        {
            return eventContainer.Partitions[partition].ToArray();
        }
        return new IAggregateEvent[] { };
    }
    private class InMemoryDocumentContainer<T>
    {
        public readonly BlockingCollection<T> All = new();
        public readonly ConcurrentDictionary<string, BlockingCollection<T>> Partitions = new();
    }
}
