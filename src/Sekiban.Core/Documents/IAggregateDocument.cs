namespace Sekiban.Core.Documents;

/// <summary>
///     Document that contains Aggregate Id
/// </summary>
public interface IAggregateDocument : IDocument
{
    /// <summary>
    ///     Aggregate Id
    /// </summary>
    Guid AggregateId { get; }
}
