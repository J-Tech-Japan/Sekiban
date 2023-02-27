using Sekiban.Core.Documents;
using Sekiban.Core.Setting;
namespace Sekiban.Core.Query.MultiProjections.Projections;

public class MultiProjectionSnapshotGenerator : IMultiProjectionSnapshotGenerator
{
    private readonly IBlobAccessor _blobAccessor;
    private readonly IDocumentRepository _documentRepository;
    public MultiProjectionSnapshotGenerator(IDocumentRepository documentRepository, IBlobAccessor blobAccessor)
    {
        _documentRepository = documentRepository;
        _blobAccessor = blobAccessor;
    }


    public Task<MultiProjectionState<TProjectionPayload>>
        GenerateMultiProjectionSnapshotAsync<TProjection, TProjectionPayload>(int minimumNumberOfEventsToGenerateSnapshot)
        where TProjection : IMultiProjector<TProjectionPayload>, new() where TProjectionPayload : IMultiProjectionPayloadCommon, new()
    {
        throw new NotImplementedException();
    }
}
