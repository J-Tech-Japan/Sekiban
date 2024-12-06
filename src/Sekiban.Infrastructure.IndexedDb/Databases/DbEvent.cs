using Sekiban.Core.Documents;
using Sekiban.Core.Events;
using Sekiban.Core.History;
using Sekiban.Core.Shared;

namespace Sekiban.Infrastructure.IndexedDb.Databases;

public record DbEvent
{
    public string Id { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
    public int Version { get; init; }
    public string CallHistories { get; init; } = string.Empty;
    public string AggregateId { get; init; } = string.Empty;
    public string PartitionKey { get; init; } = string.Empty;
    public string DocumentType { get; init; } = string.Empty;
    public string DocumentTypeName { get; init; } = string.Empty;
    public string TimeStamp { get; init; } = string.Empty;
    public string SortableUniqueId { get; init; } = string.Empty;
    public string AggregateType { get; init; } = string.Empty;
    public string RootPartitionKey { get; init; } = string.Empty;

    public static DbEvent FromEvent(IEvent ev) =>
        new()
        {
            Id = ev.Id.ToString(),
            Payload = SekibanJsonHelper.Serialize(ev.GetPayload()) ?? string.Empty,
            Version = ev.Version,
            CallHistories = SekibanJsonHelper.Serialize(ev.CallHistories) ?? string.Empty,
            AggregateId = ev.AggregateId.ToString(),
            PartitionKey = ev.PartitionKey,
            DocumentType = ev.DocumentType.ToString(),
            DocumentTypeName = ev.DocumentTypeName,
            TimeStamp = DateTimeConverter.ToString(ev.TimeStamp),
            SortableUniqueId = ev.SortableUniqueId,
            AggregateType = ev.AggregateType,
            RootPartitionKey = ev.RootPartitionKey,
        };

    public IEvent? ToEvent(RegisteredEventTypes registeredEventTypes)
    {
        if (string.IsNullOrEmpty(DocumentTypeName))
        {
            return null;
        }

        var type = registeredEventTypes.RegisteredTypes.FirstOrDefault(x => x.Name == DocumentTypeName);
        if (type is null)
        {
            return null;
        }

        if (SekibanJsonHelper.Deserialize(Payload, type) is not IEventPayloadCommon payload)
        {
            return null;
        }

        var callHistories = SekibanJsonHelper.Deserialize<List<CallHistory>>(CallHistories) ?? [];

        return Event.GenerateIEvent(
            new Guid(Id),
            new Guid(AggregateId),
            PartitionKey,
            Enum.Parse<DocumentType>(DocumentType),
            DocumentTypeName,
            type,
            DateTimeConverter.ToDateTime(TimeStamp),
            SortableUniqueId,
            payload,
            AggregateType,
            Version,
            RootPartitionKey,
            callHistories
        );
    }
}
