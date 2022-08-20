using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.Queries.MultipleAggregates
{
    public class SingleAggregateProjectionDto<Q> : IMultipleAggregateProjectionDto where Q : ISingleAggregate
    {
        public List<Q> List { get; }
        public SingleAggregateProjectionDto(List<Q> list, Guid lastEventId, string lastSortableUniqueId, int appliedSnapshotVersion, int version)
        {
            List = list;
            LastEventId = lastEventId;
            LastSortableUniqueId = lastSortableUniqueId;
            AppliedSnapshotVersion = appliedSnapshotVersion;
            Version = version;
        }
        public SingleAggregateProjectionDto() : this(new List<Q>(), Guid.Empty, string.Empty, 0, 0) { }
        public Guid LastEventId { get; }
        public string LastSortableUniqueId { get; }
        public int AppliedSnapshotVersion { get; }
        public int Version { get; }
        public List<Q> FilterList(QueryListType queryListType = QueryListType.ActiveOnly) =>
            queryListType switch
            {
                QueryListType.ActiveAndDeleted => List,
                QueryListType.ActiveOnly => List.Where(m => m.IsDeleted == false).ToList(),
                QueryListType.DeletedOnly => List.Where(m => m.IsDeleted).ToList(),
                _ => List.Where(m => m.IsDeleted == false).ToList()
            };
    }
}
