using Sekiban.EventSourcing.Documents.ValueObjects;
namespace Sekiban.EventSourcing.Queries.UpdateNotices
{
    public enum UpdatedLocationType
    {
        Local = 1,
        ExternalWeb = 2,
        ExternalFunction = 3,
        CosmosDbFeed = 4
    }
    public interface IUpdateNotice
    {
        public void SendUpdate(string aggregateName, Guid aggregateId, string sortableUniqueId, UpdatedLocationType type);
        public (bool, UpdatedLocationType?) HasUpdateAfter(string aggregateName, Guid aggregateId, SortableUniqueIdValue? sortableUniqueId);
        public (bool, UpdatedLocationType?) HasUpdateAfter(string aggregateName, SortableUniqueIdValue? sortableUniqueId);
    }
}