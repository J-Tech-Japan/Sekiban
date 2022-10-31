namespace Sekiban.Core.Document;

public enum DocumentType
{
    Command = 1,
    AggregateEvent = 2,
    AggregateSnapshot = 3,
    MultiProjectionSnapshot = 4
}
