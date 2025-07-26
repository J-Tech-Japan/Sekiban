using System.Text.Json.Nodes;
namespace Sekiban.Dcb.Events;

public record DomainEventDocumentCommon(
    Guid Id,
    JsonNode Payload,
    string SortableUniqueId,
    string PayloadTypeName,
    DateTime TimeStamp,
    DomainEventMetadata Metadata)
{
}