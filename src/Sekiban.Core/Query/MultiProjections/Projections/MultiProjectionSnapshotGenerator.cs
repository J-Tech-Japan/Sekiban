using Microsoft.Extensions.Logging;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Setting;
using Sekiban.Core.Snapshot;
using Sekiban.Core.Types;
using System.Text;
namespace Sekiban.Core.Query.MultiProjections.Projections;

public class MultiProjectionSnapshotGenerator : IMultiProjectionSnapshotGenerator
{
    private readonly IBlobAccessor _blobAccessor;
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentWriter _documentWriter;
    private readonly ILogger<MultiProjectionSnapshotGenerator> _logger;
    public MultiProjectionSnapshotGenerator(
        IDocumentRepository documentRepository,
        IBlobAccessor blobAccessor,
        IDocumentWriter documentWriter,
        ILogger<MultiProjectionSnapshotGenerator> logger)
    {
        _documentRepository = documentRepository;
        _blobAccessor = blobAccessor;
        _documentWriter = documentWriter;
        _logger = logger;
    }

    public async Task<MultiProjectionState<TProjectionPayload>> GenerateMultiProjectionSnapshotAsync<TProjection, TProjectionPayload>(
        int minimumNumberOfEventsToGenerateSnapshot,
        string rootPartitionKey) where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon, new()
    {
        var projector = new TProjection();
        // if there is snapshot, load it, if not make a new one
        var state = await GetCurrentStateAsync<TProjectionPayload>(rootPartitionKey);
        projector.ApplySnapshot(state);
        // get events from after snapshot or the initial and project them
        await _documentRepository.GetAllEventsAsync(
            typeof(TProjection),
            projector.TargetAggregateNames(),
            state.Version > 0 ? state.LastSortableUniqueId : null,
            rootPartitionKey,
            events =>
            {
                var targetSafeId = SortableUniqueIdValue.GetSafeIdFromUtc();
                foreach (var ev in events)
                {
                    if (ev.GetSortableUniqueId().IsEarlierThan(targetSafeId) &&
                        ev.GetSortableUniqueId().IsLaterThanOrEqual(projector.LastSortableUniqueId))
                    {
                        projector.ApplyEvent(ev);
                    }
                }
            });
        // save snapshot

        var usedVersion = projector.Version - state.Version;
        if (usedVersion > minimumNumberOfEventsToGenerateSnapshot)
        {
            state = projector.ToState();
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions());
            var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var blobId = Guid.NewGuid();
            await _blobAccessor.SetBlobWithGZipAsync(
                SekibanBlobContainer.MultiProjectionState,
                FilenameForSnapshot(typeof(TProjectionPayload), blobId, state.LastSortableUniqueId),
                memoryStream);
            var snapshotDocument = new MultiProjectionSnapshotDocument(typeof(TProjectionPayload), blobId, projector, rootPartitionKey);
            await _documentWriter.SaveAsync(snapshotDocument, typeof(TProjectionPayload));
            _logger.LogInformation(
                "Generate multi snapshot for {ProjectionName} and rootPartitionKey {RootPartitionKey} because used version is {UsedVersion}",
                ProjectionName(typeof(TProjectionPayload)),
                rootPartitionKey,
                usedVersion);
        } else
        {
            _logger.LogInformation(
                "skip making snapshot for {ProjectionName} and rootPartitionKey {RootPartitionKey} because used version is {UsedVersion} and minimum is {MinimumNumberOfEventsToGenerateSnapshot}",
                ProjectionName(typeof(TProjectionPayload)),
                rootPartitionKey,
                usedVersion,
                minimumNumberOfEventsToGenerateSnapshot);
        }
        return projector.ToState();
    }

    public async Task<MultiProjectionState<TProjectionPayload>> GetCurrentStateAsync<TProjectionPayload>(string rootPartitionKey)
        where TProjectionPayload : IMultiProjectionPayloadCommon, new()
    {
        var payload = new TProjectionPayload();
        var snapshotDocument = await _documentRepository.GetLatestSnapshotForMultiProjectionAsync(
            typeof(TProjectionPayload),
            payload.GetPayloadVersionIdentifier(),
            rootPartitionKey);

        // if snapshot document is not null, load it from blob storage
        if (snapshotDocument != null)
        {
            var snapshotStream = await _blobAccessor.GetBlobWithGZipAsync(
                SekibanBlobContainer.MultiProjectionState,
                FilenameForSnapshot(typeof(TProjectionPayload), snapshotDocument.Id, snapshotDocument.LastSortableUniqueId));
            if (snapshotStream != null)
            {
                using var reader = new StreamReader(snapshotStream);
                var snapshotString = await reader.ReadToEndAsync();
                var state = JsonSerializer.Deserialize<MultiProjectionState<TProjectionPayload>>(snapshotString);
                if (state != null)
                {
                    return state;
                }
            }
        }
        return new MultiProjectionState<TProjectionPayload>();
    }

    public string FilenameForSnapshot(Type projectionPayload, Guid id, SortableUniqueIdValue sortableUniqueId) =>
        $"{ProjectionName(projectionPayload)}_{sortableUniqueId.GetTicks().Ticks:00000000000000000000}_{id}.json.gz";

    private string ProjectionName(Type projectionType) =>
        projectionType.IsSingleProjectionListStateType()
            ? $"list_{projectionType.GetAggregatePayloadOrSingleProjectionPayloadTypeFromSingleProjectionListStateType().Name}"
            : projectionType.Name;
}
