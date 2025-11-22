namespace Sekiban.Dcb.Events;

public record EventMetadata(string CausationId, string CorrelationId, string ExecutedUser);
