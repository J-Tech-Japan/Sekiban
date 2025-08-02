namespace DcbLib.Events;

public record EventMetadata(
    string CausationId, 
    string CorrelationId, 
    string ExecutedUser
);