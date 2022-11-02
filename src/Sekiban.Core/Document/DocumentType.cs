namespace Sekiban.Core.Document;

public enum DocumentType
{
    Command = 1,
    Event = 2,
    AggregateSnapshot = 3,
    MultiProjectionSnapshot = 4
}
