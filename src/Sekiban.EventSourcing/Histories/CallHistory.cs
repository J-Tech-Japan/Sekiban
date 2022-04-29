namespace Sekiban.EventSourcing.Histories;

public record CallHistory(
    Guid Id,
    string TypeName,
    string? ExecutedUser
);
