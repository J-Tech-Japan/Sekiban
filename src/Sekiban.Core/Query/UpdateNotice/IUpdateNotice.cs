using Sekiban.Core.Documents.ValueObjects;
namespace Sekiban.Core.Query.UpdateNotice;

/// <summary>
///     Update notice interface.
///     Note: Update notice works only for the single server. It does not work for the multi server unless send update to
///     all servers.
/// </summary>
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
