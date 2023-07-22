using Sekiban.Core.Documents.ValueObjects;
namespace Sekiban.Core.Query.UpdateNotice;

public interface IUpdateNotice
{
    public void SendUpdate(string rootPartitionKey, string aggregateName, Guid aggregateId, string sortableUniqueId, UpdatedLocationType type);

    public (bool, UpdatedLocationType?) HasUpdateAfter(
        string rootPartitionKey,
        string aggregateName,
        Guid aggregateId,
        SortableUniqueIdValue? sortableUniqueId);

    public (bool, UpdatedLocationType?) HasUpdateAfter(string rootPartitionKey, string aggregateName, SortableUniqueIdValue? sortableUniqueId);
}
