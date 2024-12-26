using Sekiban.Core.Documents.Pools;
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
    Task SaveItemAsync<TDocument>(TDocument document, IWriteDocumentStream writeDocumentStream)
        where TDocument : IDocument;
}
