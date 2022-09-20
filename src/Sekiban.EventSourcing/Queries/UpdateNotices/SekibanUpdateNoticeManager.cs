using Sekiban.EventSourcing.Documents.ValueObjects;
using System.Collections.Concurrent;
namespace Sekiban.EventSourcing.Queries.UpdateNotices;

public class SekibanUpdateNoticeManager : IUpdateNotice
{
    private ConcurrentDictionary<string, NoticeRecord> UpdateDictionary { get; } = new();

    public void SendUpdate(string aggregateName, Guid aggregateId, string sortableUniqueId, UpdatedLocationType type)
    {
        var sortableUniqueIdValue = string.IsNullOrWhiteSpace(sortableUniqueId)
            ? new SortableUniqueIdValue(SortableUniqueIdValue.Generate(DateTime.UtcNow, Guid.Empty))
            : new SortableUniqueIdValue(sortableUniqueId);
        var toSave = new NoticeRecord(sortableUniqueIdValue, type);
        UpdateDictionary.AddOrUpdate(GetKeyForAggregate(aggregateName, aggregateId), s => toSave, (s, record) => toSave);
        UpdateDictionary.AddOrUpdate(GetKeyForType(aggregateName), s => toSave, (s, record) => toSave);
    }
    public (bool, UpdatedLocationType?) HasUpdateAfter(string aggregateName, Guid aggregateId, SortableUniqueIdValue? sortableUniqueId)
    {
        var current = UpdateDictionary.GetValueOrDefault(GetKeyForAggregate(aggregateName, aggregateId));
        if (current is null || string.IsNullOrEmpty(current.SortableUniqueId))
        {
            return (false, null);
        }
        return (!current.SortableUniqueId.Value?.Equals(sortableUniqueId?.Value ?? string.Empty) ?? true, current?.LocationType);
    }
    public (bool, UpdatedLocationType?) HasUpdateAfter(string aggregateName, SortableUniqueIdValue? sortableUniqueId)
    {
        var current = UpdateDictionary.GetValueOrDefault(GetKeyForType(aggregateName));
        if (current is null || string.IsNullOrEmpty(current.SortableUniqueId))
        {
            return (false, null);
        }
        if (sortableUniqueId is null)
        {
            return (false, null);
        }
        return (!current.SortableUniqueId.Value?.Equals(sortableUniqueId) ?? true, current?.LocationType);
    }

    public static string GetKeyForAggregate(string aggregateName, Guid aggregateId)
    {
        return "UpdateNotice-" + aggregateName + "-" + aggregateId;
    }
    public static string GetKeyForType(string aggregateName)
    {
        return "UpdateNotice-" + aggregateName + "-";
    }
    private record NoticeRecord(SortableUniqueIdValue SortableUniqueId, UpdatedLocationType LocationType);
}
