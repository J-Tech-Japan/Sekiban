namespace Sekiban.Core.Documents;

public interface IAggregateDocument : IDocument
{
    Guid AggregateId { get; }
}
