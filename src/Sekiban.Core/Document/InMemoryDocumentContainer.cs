using System.Collections.Concurrent;
using Sekiban.Core.Event;

namespace Sekiban.Core.Document;

public class InMemoryDocumentStore
{
    private static readonly SemaphoreSlim _semaphoreInMemory = new(1, 1);
    private readonly ConcurrentDictionary<string, InMemoryDocumentContainer<IEvent>> _containerDictionary = new();

    public void ResetInMemoryStore()
    {
        _containerDictionary.Clear();
    }

    public void SaveEvent(IEvent document, string partition, string sekibanContextIdentifier)
    {
        _semaphoreInMemory.Wait();
        if (!_containerDictionary.ContainsKey(sekibanContextIdentifier))
            _containerDictionary[sekibanContextIdentifier] = new InMemoryDocumentContainer<IEvent>();
        var eventContainer = _containerDictionary[sekibanContextIdentifier];
        eventContainer.All.Add(document);
        if (eventContainer.Partitions.ContainsKey(partition))
        {
            if (!eventContainer.Partitions[partition].Any(m => m.Id == document.Id))
                eventContainer.Partitions[partition].Add(document);
        }
        else
        {
            var partitionCollection = new BlockingCollection<IEvent>();
            partitionCollection.Add(document);
            eventContainer.Partitions[partition] = partitionCollection;
        }

        if (!eventContainer.All.Any(m => m.PartitionKey == document.PartitionKey && m.Id == document.Id))
            eventContainer.All.Add(document);
        _semaphoreInMemory.Release();
    }

    public IEvent[] GetAllEvents(string sekibanContextIdentifier)
    {
        if (!_containerDictionary.ContainsKey(sekibanContextIdentifier))
            _containerDictionary[sekibanContextIdentifier] = new InMemoryDocumentContainer<IEvent>();
        var eventContainer = _containerDictionary[sekibanContextIdentifier];
        return eventContainer.All.ToArray();
    }

    public IEvent[] GetEventPartition(string partition, string sekibanContextIdentifier)
    {
        if (!_containerDictionary.ContainsKey(sekibanContextIdentifier))
            _containerDictionary[sekibanContextIdentifier] = new InMemoryDocumentContainer<IEvent>();
        var eventContainer = _containerDictionary[sekibanContextIdentifier];
        if (eventContainer.Partitions.ContainsKey(partition)) return eventContainer.Partitions[partition].ToArray();
        return new IEvent[] { };
    }

    private class InMemoryDocumentContainer<T>
    {
        public readonly BlockingCollection<T> All = new();
        public readonly ConcurrentDictionary<string, BlockingCollection<T>> Partitions = new();
    }
}
