namespace Sekiban.Dcb.ColdEvents;

public record ColdCheckpoint(
    string ServiceId,
    string? NextSinceSortableUniqueId,
    DateTimeOffset UpdatedAtUtc);
