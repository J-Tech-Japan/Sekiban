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
    Task SaveAsync<TDocument>(TDocument document, Type aggregateType) where TDocument : IDocument;
    /// <summary>
    ///     Save and Publish event to in machine MediatR.
    /// </summary>
    /// <param name="events"></param>
    /// <param name="aggregateType"></param>
    /// <typeparam name="TEvent"></typeparam>
    /// <returns></returns>
    Task SaveAndPublishEvents<TEvent>(IEnumerable<TEvent> events, Type aggregateType) where TEvent : IEvent;
}
