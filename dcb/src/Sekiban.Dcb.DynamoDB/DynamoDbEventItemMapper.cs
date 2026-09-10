using System.Security.Cryptography;
using System.Text;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.DynamoDB.Models;

namespace Sekiban.Dcb.DynamoDB;

/// <summary>
/// The one provider-owned event-item mapper used by typed, serialized, and conditional writes.
/// Keeping the mapper at the write boundary makes the measured item the item that the store emits.
/// </summary>
internal static class DynamoDbEventItemMapper
{
    public static DynamoDbEventWriteItem FromEvent(
        Event @event,
        string serializedPayload,
        string serviceId,
        int writeShardCount,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(serializedPayload);
        ArgumentNullException.ThrowIfNull(serviceId);

        var timestampText = timestamp.ToString("O");
        var dynamoEvent = DynamoEvent.FromEvent(
            @event,
            serializedPayload,
            GetGsiPartitionKey(@event.SortableUniqueIdValue, serviceId, writeShardCount),
            serviceId);
        dynamoEvent.Timestamp = timestampText;

        var tags = @event.Tags.Select(tagString =>
        {
            var tagGroup = tagString.Contains(':', StringComparison.Ordinal)
                ? tagString.Split(':')[0]
                : tagString;
            var tag = DynamoTag.FromEventTag(
                serviceId,
                tagString,
                BuildStoredTagGroup(serviceId, tagGroup),
                @event.SortableUniqueIdValue,
                @event.Id,
                @event.EventType);
            tag.CreatedAt = timestampText;
            return tag;
        }).ToList();

        return new DynamoDbEventWriteItem(@event, dynamoEvent, tags);
    }

    public static DynamoDbEventWriteItem FromSerializableEvent(
        SerializableEvent serializedEvent,
        string serviceId,
        int writeShardCount,
        Guid? eventId = null,
        DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(serializedEvent);

        var helperEvent = new Event(
            PlaceholderPayload.Instance,
            serializedEvent.SortableUniqueIdValue,
            serializedEvent.EventPayloadName,
            eventId ?? serializedEvent.Id,
            serializedEvent.EventMetadata,
            serializedEvent.Tags);

        return FromEvent(
            helperEvent,
            Encoding.UTF8.GetString(serializedEvent.Payload),
            serviceId,
            writeShardCount,
            timestamp ?? DateTimeOffset.UtcNow);
    }

    private static string GetGsiPartitionKey(string sortableUniqueId, string serviceId, int writeShardCount)
    {
        if (writeShardCount <= 1)
            return BuildEventsGsiPartitionKey(serviceId);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sortableUniqueId));
        var shard = (BitConverter.ToInt32(hash, 0) & int.MaxValue) % writeShardCount;
        return BuildEventsGsiPartitionKey(serviceId, shard);
    }

    private static string BuildEventsGsiPartitionKey(string serviceId, int? shard = null)
    {
        var baseKey = $"SERVICE#{serviceId}#{DynamoDbContext.EventsGsiPartitionKey}";
        return shard.HasValue ? $"{baseKey}#{shard.Value}" : baseKey;
    }

    private static string BuildStoredTagGroup(string serviceId, string tagGroup) =>
        $"{serviceId}|{tagGroup}";

    private sealed class PlaceholderPayload : IEventPayload
    {
        public static readonly PlaceholderPayload Instance = new();
    }
}

internal sealed record DynamoDbEventWriteItem(
    Event Event,
    DynamoEvent DynamoEvent,
    List<DynamoTag> DynamoTags);
