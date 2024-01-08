using Microsoft.Extensions.Logging;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Setting;
using Sekiban.Core.Snapshot;
using Sekiban.Core.Types;
using System.Reflection;
using System.Text;
namespace Sekiban.Core.Query.MultiProjections.Projections;

/// <summary>
///     Multi Projection Snapshot Generator
/// </summary>
public class MultiProjectionSnapshotGenerator(
    IDocumentRepository documentRepository,
    IBlobAccessor blobAccessor,
    IDocumentWriter documentWriter,
    ILogger<MultiProjectionSnapshotGenerator> logger) : IMultiProjectionSnapshotGenerator
{

    public async Task<MultiProjectionState<TProjectionPayload>> GenerateMultiProjectionSnapshotAsync<TProjection, TProjectionPayload>(
        int minimumNumberOfEventsToGenerateSnapshot,
        string rootPartitionKey) where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon
    {
        var projector = new TProjection();
        // if there is snapshot, load it, if not make a new one
        var state = await GetCurrentStateAsync<TProjectionPayload>(rootPartitionKey);
        if (state.Version > 0)
        {
            projector.ApplySnapshot(state);
        }
        // get events from after snapshot or the initial and project them
        await documentRepository.GetAllEventsAsync(
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
            await blobAccessor.SetBlobWithGZipAsync(
                SekibanBlobContainer.MultiProjectionState,
                FilenameForSnapshot(typeof(TProjectionPayload), blobId, state.LastSortableUniqueId),
                memoryStream);
            var snapshotDocument = new MultiProjectionSnapshotDocument(typeof(TProjectionPayload), blobId, projector, rootPartitionKey);
            await documentWriter.SaveAsync(snapshotDocument, typeof(TProjectionPayload));
            logger.LogInformation(
                "Generate multi snapshot for {ProjectionName} and rootPartitionKey {RootPartitionKey} because used version is {UsedVersion}",
                MultiProjectionSnapshotGenerator.ProjectionName(typeof(TProjectionPayload)),
                rootPartitionKey,
                usedVersion);
        } else
        {
            logger.LogInformation(
                "skip making snapshot for {ProjectionName} and rootPartitionKey {RootPartitionKey} because used version is {UsedVersion} and minimum is {MinimumNumberOfEventsToGenerateSnapshot}",
                MultiProjectionSnapshotGenerator.ProjectionName(typeof(TProjectionPayload)),
                rootPartitionKey,
                usedVersion,
                minimumNumberOfEventsToGenerateSnapshot);
        }
        return projector.ToState();
    }
    public async Task<MultiProjectionState<TProjectionPayload>> GetCurrentStateAsync<TProjectionPayload>(string rootPartitionKey)
        where TProjectionPayload : IMultiProjectionPayloadCommon
    {
        var payload = GeneratePayload<TProjectionPayload>();
        var snapshotDocument = await documentRepository.GetLatestSnapshotForMultiProjectionAsync(
            typeof(TProjectionPayload),
            payload.GetPayloadVersionIdentifier(),
            rootPartitionKey);

        // if snapshot document is not null, load it from blob storage
        if (snapshotDocument != null)
        {
            try
            {
                var snapshotStream = await blobAccessor.GetBlobWithGZipAsync(
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
            catch
            {
                return new MultiProjectionState<TProjectionPayload>();
            }
        }
        return new MultiProjectionState<TProjectionPayload>();
    }
    private static TProjectionPayload GeneratePayload<TProjectionPayload>() where TProjectionPayload : IMultiProjectionPayloadCommon
    {
        var payloadType = typeof(TProjectionPayload);
        if (payloadType.IsMultiProjectionPayloadType())
        {
            var method = payloadType.GetMethod(
                nameof(IMultiProjectionPayloadGeneratePayload<TProjectionPayload>.CreateInitialPayload),
                BindingFlags.Static | BindingFlags.Public);
            var created = method?.Invoke(payloadType, new object?[] { });
            return created is TProjectionPayload projectionPayload
                ? projectionPayload
                : throw new SekibanMultiProjectionPayloadCreateFailedException(payloadType.FullName ?? "");
        }
        throw new SekibanMultiProjectionPayloadCreateFailedException(payloadType.FullName ?? "");
    }

    public string FilenameForSnapshot(Type projectionPayload, Guid id, SortableUniqueIdValue sortableUniqueId) =>
        $"{MultiProjectionSnapshotGenerator.ProjectionName(projectionPayload)}_{sortableUniqueId.GetTicks().Ticks:00000000000000000000}_{id}.json.gz";

    private static string ProjectionName(Type projectionType) =>
        projectionType.IsSingleProjectionListStateType()
            ? $"list_{projectionType.GetAggregatePayloadOrSingleProjectionPayloadTypeFromSingleProjectionListStateType().Name}"
            : projectionType.Name;
}
