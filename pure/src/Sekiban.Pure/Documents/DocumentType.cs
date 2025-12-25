namespace Sekiban.Pure.Documents;

/// <summary>
///     Document Type : used in Documents, what type of document
/// </summary>
public enum DocumentType
{
    Command = 1, Event = 2, AggregateSnapshot = 3, MultiProjectionSnapshot = 4
}
