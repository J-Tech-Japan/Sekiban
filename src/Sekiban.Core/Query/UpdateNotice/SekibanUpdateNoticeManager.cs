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

    public SekibanUpdateNoticeManager(ISekibanDateProducer sekibanDateProducer) =>
        _sekibanDateProducer = sekibanDateProducer;

    public void SendUpdate(
        string rootPartitionKey,
        string aggregateName,
        Guid aggregateId,
        string sortableUniqueId,
        UpdatedLocationType type)
    {
        var sortableUniqueIdValue = string.IsNullOrWhiteSpace(sortableUniqueId)
            ? new SortableUniqueIdValue(SortableUniqueIdValue.Generate(_sekibanDateProducer.UtcNow, Guid.Empty))
            : new SortableUniqueIdValue(sortableUniqueId);
        var toSave = new NoticeRecord(sortableUniqueIdValue, type);
        UpdateDictionary.AddOrUpdate(
            GetKeyForAggregate(rootPartitionKey, aggregateName, aggregateId),
            _ => toSave,
            (_, _) => toSave);
        UpdateDictionary.AddOrUpdate(GetKeyForType(rootPartitionKey, aggregateName), _ => toSave, (_, _) => toSave);
        UpdateDictionary.AddOrUpdate(GetKeyForType(aggregateName), _ => toSave, (_, _) => toSave);
    }

    public (bool, UpdatedLocationType?) HasUpdateAfter(
        string rootPartitionKey,
        string aggregateName,
        Guid aggregateId,
        SortableUniqueIdValue? sortableUniqueId) =>
        HasUpdateAfter(
            UpdateDictionary.GetValueOrDefault(GetKeyForAggregate(rootPartitionKey, aggregateName, aggregateId)),
            sortableUniqueId);

    public (bool, UpdatedLocationType?) HasUpdateAfter(
        string rootPartitionKey,
        string aggregateName,
        SortableUniqueIdValue? sortableUniqueId) =>
        HasUpdateAfter(
            rootPartitionKey.Equals(IMultiProjectionService.ProjectionAllRootPartitions)
                ? UpdateDictionary.GetValueOrDefault(GetKeyForType(aggregateName))
                : UpdateDictionary.GetValueOrDefault(GetKeyForType(rootPartitionKey, aggregateName)),
            sortableUniqueId);

    private static (bool, UpdatedLocationType?) HasUpdateAfter(
        NoticeRecord? noticeRecord,
        SortableUniqueIdValue? sortableUniqueId)
    {
        return (noticeRecord, sortableUniqueId) switch
        {
            (null, _) => (false, null),
            ({ SortableUniqueId: null }, _) => (false, null),
            ({ SortableUniqueId.Value: "" }, _) => (false, null),
            (_, null) => (true, null),
            (_, { Value: "" }) => (true, null),
            ({ } c, { } s) => ((bool, UpdatedLocationType?))(s.IsEarlierThanOrEqual(c.SortableUniqueId), c.LocationType)
        };
    }

    public static string GetKeyForAggregate(string rootPartitionKey, string aggregateName, Guid aggregateId) =>
        "UpdateNotice-" + rootPartitionKey + "-" + aggregateName + "-" + aggregateId;

    public static string GetKeyForType(string rootPartitionKey, string aggregateName) =>
        "UpdateNotice-" + rootPartitionKey + "-" + aggregateName;

    public static string GetKeyForType(string aggregateName) => "UpdateNotice-" + aggregateName;

    private record NoticeRecord(SortableUniqueIdValue SortableUniqueId, UpdatedLocationType LocationType);
}
