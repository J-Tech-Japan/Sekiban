using Sekiban.Core.Snapshot;
namespace Sekiban.Core.Documents;

/// <summary>
///     Document persistent writer for document database like Cosmos, Dynamo
/// </summary>
public interface IDocumentPersistentWriter : IDocumentWriter
{

    /// <summary>
    ///     save snapshot
    /// </summary>
    /// <param name="document"></param>
    /// <param name="aggregateType"></param>
    /// <param name="useBlob"></param>
    /// <returns></returns>
    Task SaveSingleSnapshotAsync(SnapshotDocument document, Type aggregateType, bool useBlob);

    /// <summary>
    ///     Document Store can not save document size, thus need to save payload to the blob
    /// </summary>
    /// <param name="document"></param>
    /// <returns></returns>
    bool ShouldUseBlob(SnapshotDocument document);
}
