namespace Sekiban.Dcb.Tags;

public record TagStream(
    string Tag,
    Guid EventId,
    string SortableUniqueId
);