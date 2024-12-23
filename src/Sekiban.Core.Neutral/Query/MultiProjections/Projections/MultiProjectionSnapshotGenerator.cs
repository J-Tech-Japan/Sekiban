using Microsoft.Extensions.Logging;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.Pools;
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
    EventRepository eventRepository,
    IDocumentRepository documentRepository,
    IBlobAccessor blobAccessor,
    IDocumentWriter documentWriter,
    ILogger<MultiProjectionSnapshotGenerator> logger) : IMultiProjectionSnapshotGenerator
{
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new();

    public async Task<MultiProjectionState<TProjectionPayload>>
        GenerateMultiProjectionSnapshotAsync<TProjection, TProjectionPayload>(
            int minimumNumberOfEventsToGenerateSnapshot,
            string rootPartitionKey = IMultiProjectionService.ProjectionAllRootPartitions)
        where TProjection : IMultiProjector<TProjectionPayload>, new()
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
        await eventRepository.GetEvents(
            EventRetrievalInfo.FromNullableValues(
                rootPartitionKey,
                new MultiProjectionTypeStream(typeof(TProjection), projector.TargetAggregateNames()),
                null,
                ISortableIdCondition.FromMultiProjectionState(state)),
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
            var json = JsonSerializer.Serialize(state, _jsonSerializerOptions);
            var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var blobId = Guid.NewGuid();
            await blobAccessor.SetBlobWithGZipAsync(
                SekibanBlobContainer.MultiProjectionState,
                FilenameForSnapshot(typeof(TProjectionPayload), blobId, state.LastSortableUniqueId),
                memoryStream);
            var snapshotDocument = new MultiProjectionSnapshotDocument(
                typeof(TProjectionPayload),
                blobId,
                projector,
                rootPartitionKey);
            await documentWriter.SaveItemAsync(snapshotDocument, new AggregateWriteStream(typeof(TProjectionPayload)));
            logger.LogInformation(
                "Generate multi snapshot for {ProjectionName} and rootPartitionKey {RootPartitionKey} because state version is {StateVersion}, and number of events after the state is {UsedVersion}",
                ProjectionName(typeof(TProjectionPayload)),
                rootPartitionKey,
                state.Version,
                usedVersion);
        } else
        {
            logger.LogInformation(
                "skip making snapshot for {ProjectionName} and rootPartitionKey {RootPartitionKey} because state version is {StateVersion} and events after that is {UsedVersion} and minimum is {MinimumNumberOfEventsToGenerateSnapshot}",
                ProjectionName(typeof(TProjectionPayload)),
                rootPartitionKey,
                state.Version,
                usedVersion,
                minimumNumberOfEventsToGenerateSnapshot);
        }
        return projector.ToState();
    }

    public async Task<MultiProjectionState<TProjectionPayload>> GetCurrentStateAsync<TProjectionPayload>(
        string rootPartitionKey) where TProjectionPayload : IMultiProjectionPayloadCommon
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
                    FilenameForSnapshot(
                        typeof(TProjectionPayload),
                        snapshotDocument.Id,
                        snapshotDocument.LastSortableUniqueId));
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

    private static TProjectionPayload GeneratePayload<TProjectionPayload>()
        where TProjectionPayload : IMultiProjectionPayloadCommon
    {
        var payloadType = typeof(TProjectionPayload);
        if (payloadType.IsMultiProjectionPayloadType())
        {
            var method = payloadType.GetMethod(
                nameof(IMultiProjectionPayloadGeneratePayload<TProjectionPayload>.CreateInitialPayload),
                BindingFlags.Static | BindingFlags.Public);
            var created = method?.Invoke(payloadType, []);
            return created is TProjectionPayload projectionPayload
                ? projectionPayload
                : throw new SekibanMultiProjectionPayloadCreateFailedException(payloadType.FullName ?? "");
        }
        throw new SekibanMultiProjectionPayloadCreateFailedException(payloadType.FullName ?? "");
    }

    public static string FilenameForSnapshot(Type projectionPayload, Guid id, SortableUniqueIdValue sortableUniqueId) =>
        $"{ProjectionName(projectionPayload)}_{sortableUniqueId.GetTicks().Ticks:00000000000000000000}_{id}.json.gz";

    private static string ProjectionName(Type projectionType) =>
        projectionType.IsSingleProjectionListStateType()
            ? $"list_{projectionType.GetAggregatePayloadOrSingleProjectionPayloadTypeFromSingleProjectionListStateType().Name}"
            : projectionType.Name;
}
