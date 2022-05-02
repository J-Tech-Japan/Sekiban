using System.Collections.Concurrent;
namespace Sekiban.EventSourcing.Documents;


public class InMemoryDocumentStore
{
    private class InMemoryDocumentContainer<T>
    {
        public readonly BlockingCollection<T> All = new BlockingCollection<T>();
        public readonly ConcurrentDictionary<string, BlockingCollection<T>> Partitions =
            new ConcurrentDictionary<string, BlockingCollection<T>>();
    }

    private InMemoryDocumentContainer<AggregateEvent> _eventContainer = new InMemoryDocumentContainer<AggregateEvent>();
    private InMemoryDocumentContainer<Document> _otherContainer = new InMemoryDocumentContainer<Document>();

    public void SaveItem(Document document, string partition)
    {
        _otherContainer.All.Add(document);
        if (_otherContainer.Partitions.ContainsKey(partition))
        {
            _otherContainer.Partitions[partition].Add(document);
        }
        else
        {
            var partitionCollection = new BlockingCollection<Document>();
            partitionCollection.Add(document);
            _otherContainer.Partitions[partition] = partitionCollection;
        }
    }
    public void SaveEvent(AggregateEvent document, string partition)
    {
        _eventContainer.All.Add(document);
        if (_eventContainer.Partitions.ContainsKey(partition))
        {
            _eventContainer.Partitions[partition].Add(document);
        }
        else
        {
            var partitionCollection = new BlockingCollection<AggregateEvent>();
            partitionCollection.Add(document);
            _eventContainer.Partitions[partition] = partitionCollection;
        }
    }
    public Document[] GetAllItems() =>
        _otherContainer.All.ToArray();
    public Document[] GetItemPartition(string partition)  {
        if (_eventContainer.Partitions.ContainsKey(partition))
        {
            return _otherContainer.Partitions[partition].ToArray();
        }
        return new Document[] {};
    }
    public AggregateEvent[] GetAllEvents() =>
        _eventContainer.All.ToArray();
    public AggregateEvent[] GetEventPartition(string partition) {
        if (_eventContainer.Partitions.ContainsKey(partition))
        {
            return _eventContainer.Partitions[partition].ToArray();
        }
        return new AggregateEvent[] {};
    }
}
