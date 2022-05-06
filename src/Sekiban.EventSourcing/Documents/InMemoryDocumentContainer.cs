using System.Collections.Concurrent;
namespace Sekiban.EventSourcing.Documents;

public class InMemoryDocumentStore
{
    private readonly InMemoryDocumentContainer<AggregateEvent> _eventContainer = new();

    public void SaveEvent(AggregateEvent document, string partition)
    {
        _eventContainer.All.Add(document);
        if (_eventContainer.Partitions.ContainsKey(partition))
        {
            if (!_eventContainer.Partitions[partition].Any(m => m.Id == document.Id))
            {
                _eventContainer.Partitions[partition].Add(document);
            }
        } else
        {
            var partitionCollection = new BlockingCollection<AggregateEvent>();
            partitionCollection.Add(document);
            _eventContainer.Partitions[partition] = partitionCollection;
        }
    }
    public void ResetEventsForPartition(string partitionKey, string beforeSortableUniqueId)
    {
        var partitionContainer = _eventContainer.Partitions[partitionKey];
        var toAdd = new List<AggregateEvent>();

        while (partitionContainer.TryTake(out var checkingItem))
        {
            if (!string.IsNullOrWhiteSpace(beforeSortableUniqueId) && checkingItem.SortableUniqueId.CompareTo(beforeSortableUniqueId) > 0)
            {
                toAdd.Add(checkingItem);
            }
        }
        foreach (var checkingItem in toAdd) { partitionContainer.Add(checkingItem); }
    }
    public AggregateEvent[] GetAllEvents() =>
        _eventContainer.All.ToArray();
    public AggregateEvent[] GetEventPartition(string partition)
    {
        if (_eventContainer.Partitions.ContainsKey(partition))
        {
            return _eventContainer.Partitions[partition].ToArray();
        }
        return new AggregateEvent[] { };
    }
    private class InMemoryDocumentContainer<T>
    {
        public readonly BlockingCollection<T> All = new();
        public readonly ConcurrentDictionary<string, BlockingCollection<T>> Partitions = new();
    }
}
