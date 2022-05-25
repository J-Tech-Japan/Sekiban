using System.Collections.Concurrent;
namespace Sekiban.EventSourcing.Documents;

public class InMemoryDocumentStore
{
    private readonly ConcurrentDictionary<string, InMemoryDocumentContainer<AggregateEvent>> _containerDictionary = new();
    public void ResetInMemoryStore()
    {
        _containerDictionary.Clear();
    }
    public void SaveEvent(AggregateEvent document, string partition, string sekibanContextIdentifier)
    {
        if (!_containerDictionary.ContainsKey(sekibanContextIdentifier))
        {
            _containerDictionary[sekibanContextIdentifier] = new InMemoryDocumentContainer<AggregateEvent>();
        }
        var eventContainer = _containerDictionary[sekibanContextIdentifier];
        eventContainer.All.Add(document);
        if (eventContainer.Partitions.ContainsKey(partition))
        {
            if (!eventContainer.Partitions[partition].Any(m => m.Id == document.Id))
            {
                eventContainer.Partitions[partition].Add(document);
            }
        } else
        {
            var partitionCollection = new BlockingCollection<AggregateEvent>();
            partitionCollection.Add(document);
            eventContainer.Partitions[partition] = partitionCollection;
        }
        if (!eventContainer.All.Any(m => m.PartitionKey == document.PartitionKey && m.Id == document.Id))
        {
            eventContainer.All.Add(document);
        }
    }
    public AggregateEvent[] GetAllEvents(string sekibanContextIdentifier)
    {
        if (!_containerDictionary.ContainsKey(sekibanContextIdentifier))
        {
            _containerDictionary[sekibanContextIdentifier] = new InMemoryDocumentContainer<AggregateEvent>();
        }
        var eventContainer = _containerDictionary[sekibanContextIdentifier];
        return eventContainer.All.ToArray();
    }
    public AggregateEvent[] GetEventPartition(string partition, string sekibanContextIdentifier)
    {
        if (!_containerDictionary.ContainsKey(sekibanContextIdentifier))
        {
            _containerDictionary[sekibanContextIdentifier] = new InMemoryDocumentContainer<AggregateEvent>();
        }
        var eventContainer = _containerDictionary[sekibanContextIdentifier];
        if (eventContainer.Partitions.ContainsKey(partition))
        {
            return eventContainer.Partitions[partition].ToArray();
        }
        return new AggregateEvent[] { };
    }
    private class InMemoryDocumentContainer<T>
    {
        public readonly BlockingCollection<T> All = new();
        public readonly ConcurrentDictionary<string, BlockingCollection<T>> Partitions = new();
    }
}
