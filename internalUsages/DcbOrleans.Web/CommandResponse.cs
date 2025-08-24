namespace DcbOrleans.Web;

public record CommandResponse(bool Success, Guid? EventId, Guid? AggregateId, string? Error, string? SortableUniqueId);
