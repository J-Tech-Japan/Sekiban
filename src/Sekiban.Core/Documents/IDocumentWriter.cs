using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Events;
namespace Sekiban.Core.Documents;

/// <summary>
///     Document Writer interface
/// </summary>
public interface IDocumentWriter
{
    /// <summary>
    ///     Save document
    /// </summary>
    /// <param name="document"></param>
    /// <param name="aggregateType"></param>
    /// <typeparam name="TDocument"></typeparam>
    /// <returns></returns>
    Task SaveAsync<TDocument>(TDocument document, IWriteDocumentStream writeDocumentStream) where TDocument : IDocument;
    /// <summary>
    ///     Save and Publish event to in machine MediatR.
    /// </summary>
    /// <param name="events"></param>
    /// <param name="aggregateType"></param>
    /// <typeparam name="TEvent"></typeparam>
    /// <returns></returns>
    Task SaveAndPublishEvents<TEvent>(IEnumerable<TEvent> events, IWriteDocumentStream writeDocumentStream)
        where TEvent : IEvent;
}
