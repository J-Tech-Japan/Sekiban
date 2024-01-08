using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Shared;
using System.Collections.Concurrent;
namespace Sekiban.Core.Query.UpdateNotice;

/// <summary>
///     Update notice manager for Sekiban.
/// </summary>
public class SekibanUpdateNoticeManager : IUpdateNotice
{
    private readonly ISekibanDateProducer _sekibanDateProducer;

    private ConcurrentDictionary<string, NoticeRecord> UpdateDictionary { get; } = new();

    public SekibanUpdateNoticeManager(ISekibanDateProducer sekibanDateProducer) => _sekibanDateProducer = sekibanDateProducer;

    public void SendUpdate(string rootPartitionKey, string aggregateName, Guid aggregateId, string sortableUniqueId, UpdatedLocationType type)
    {
        var sortableUniqueIdValue = string.IsNullOrWhiteSpace(sortableUniqueId)
            ? new SortableUniqueIdValue(SortableUniqueIdValue.Generate(_sekibanDateProducer.UtcNow, Guid.Empty))
            : new SortableUniqueIdValue(sortableUniqueId);
        var toSave = new NoticeRecord(sortableUniqueIdValue, type);
        UpdateDictionary.AddOrUpdate(GetKeyForAggregate(rootPartitionKey, aggregateName, aggregateId), _ => toSave, (_, _) => toSave);
        UpdateDictionary.AddOrUpdate(GetKeyForType(rootPartitionKey, aggregateName), _ => toSave, (_, _) => toSave);
        UpdateDictionary.AddOrUpdate(GetKeyForType(aggregateName), _ => toSave, (_, _) => toSave);
    }

    public (bool, UpdatedLocationType?) HasUpdateAfter(
        string rootPartitionKey,
        string aggregateName,
        Guid aggregateId,
        SortableUniqueIdValue? sortableUniqueId)
    {
        var current = UpdateDictionary.GetValueOrDefault(GetKeyForAggregate(rootPartitionKey, aggregateName, aggregateId));
        return current is null || string.IsNullOrEmpty(current.SortableUniqueId)
            ? (false, null)
            : string.IsNullOrEmpty(sortableUniqueId?.Value)
            ? (true, null)
            : ((bool, UpdatedLocationType?))(sortableUniqueId.IsEarlierThanOrEqual(current.SortableUniqueId), current.LocationType);
    }

    public (bool, UpdatedLocationType?) HasUpdateAfter(string rootPartitionKey, string aggregateName, SortableUniqueIdValue? sortableUniqueId)
    {
        if (rootPartitionKey.Equals(IMultiProjectionService.ProjectionAllRootPartitions))
        {
            var currentAll = UpdateDictionary.GetValueOrDefault(GetKeyForType(aggregateName));
            return currentAll is null || string.IsNullOrEmpty(currentAll.SortableUniqueId)
                ? (false, null)
                : sortableUniqueId is null
                ? (true, null)
                : ((bool, UpdatedLocationType?))(sortableUniqueId.IsEarlierThanOrEqual(currentAll.SortableUniqueId), currentAll.LocationType);
        }
        var current = UpdateDictionary.GetValueOrDefault(GetKeyForType(rootPartitionKey, aggregateName));
        return current is null || string.IsNullOrEmpty(current.SortableUniqueId)
            ? (false, null)
            : sortableUniqueId is null
            ? (true, null)
            : ((bool, UpdatedLocationType?))(sortableUniqueId.IsEarlierThanOrEqual(current.SortableUniqueId), current.LocationType);
    }

    public static string GetKeyForAggregate(string rootPartitionKey, string aggregateName, Guid aggregateId) =>
        "UpdateNotice-" + rootPartitionKey + "-" + aggregateName + "-" + aggregateId;

    public static string GetKeyForType(string rootPartitionKey, string aggregateName) => "UpdateNotice-" + rootPartitionKey + "-" + aggregateName;
    public static string GetKeyForType(string aggregateName) => "UpdateNotice-" + aggregateName;

    private record NoticeRecord(SortableUniqueIdValue SortableUniqueId, UpdatedLocationType LocationType);
}
