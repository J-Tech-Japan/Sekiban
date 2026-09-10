using System.Text;
using Sekiban.Dcb.CosmosDb.Models;
using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.CosmosDb;

/// <summary>
/// The single construction core for the Cosmos event document emitted by every event-write entry.
/// </summary>
internal static class CosmosEventDocumentMapper
{
    private static readonly DateTime MeasurementTimestamp =
        new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(1_234_567);

    internal static DateTime MeasurementTimestampUtc => MeasurementTimestamp;

    internal static CosmosEvent FromEvent(
        Event ev,
        string serializedPayload,
        string serviceId,
        DateTime utcTimestamp)
    {
        ArgumentNullException.ThrowIfNull(ev);
        ArgumentNullException.ThrowIfNull(serializedPayload);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);

        return FromParts(
            serviceId,
            ev.Id,
            ev.SortableUniqueIdValue,
            ev.EventType,
            serializedPayload,
            ev.Tags,
            ev.EventMetadata,
            utcTimestamp);
    }

    internal static CosmosEvent FromSerializableEvent(
        SerializableEvent serializedEvent,
        string serviceId,
        DateTime utcTimestamp)
    {
        ArgumentNullException.ThrowIfNull(serializedEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);

        return FromParts(
            serviceId,
            serializedEvent.Id,
            serializedEvent.SortableUniqueIdValue,
            serializedEvent.EventPayloadName,
            Encoding.UTF8.GetString(serializedEvent.Payload),
            serializedEvent.Tags,
            serializedEvent.EventMetadata,
            utcTimestamp);
    }

    internal static CosmosEvent FromParts(
        string serviceId,
        Guid documentId,
        string sortableUniqueId,
        string eventType,
        string serializedPayload,
        IReadOnlyList<string> tags,
        EventMetadata metadata,
        DateTime utcTimestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sortableUniqueId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentNullException.ThrowIfNull(serializedPayload);
        ArgumentNullException.ThrowIfNull(tags);
        if (utcTimestamp.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Cosmos event document timestamps must be UTC.", nameof(utcTimestamp));
        }

        var id = documentId.ToString();
        return new CosmosEvent
        {
            Pk = $"{serviceId}|{id}",
            ServiceId = serviceId,
            Id = id,
            SortableUniqueId = sortableUniqueId,
            EventType = eventType,
            Payload = serializedPayload,
            Tags = tags,
            Timestamp = utcTimestamp,
            CausationId = metadata.CausationId,
            CorrelationId = metadata.CorrelationId,
            ExecutedUser = metadata.ExecutedUser
        };
    }
}
