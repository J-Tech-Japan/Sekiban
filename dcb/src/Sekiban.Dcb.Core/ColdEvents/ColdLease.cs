namespace Sekiban.Dcb.ColdEvents;

public record ColdLease(
    string LeaseId,
    string Token,
    DateTimeOffset ExpiresAt);
