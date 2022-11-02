using Sekiban.Core.Document;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Partition;
namespace Sekiban.Core.Query.SingleProjections.Projections;

public class SimpleSingleProjectionFromInitial : ISingleProjectionFromInitial
{
    private readonly IDocumentRepository _documentRepository;
    public SimpleSingleProjectionFromInitial(IDocumentRepository documentRepository) => _documentRepository = documentRepository;
    /// <summary>
    ///     メモリキャッシュも使用せず、初期イベントからAggregateを作成します。
    ///     遅いので、通常はキャッシュバージョンを使用ください
    ///     検証などのためにこちらを残しています。
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="toVersion"></param>
    /// <typeparam name="TProjection"></typeparam>
    /// <typeparam name="TProjector"></typeparam>
    /// <returns></returns>
    public async Task<TProjection?> GetAggregateFromInitialAsync<TProjection, TProjector>(Guid aggregateId, int? toVersion)
        where TProjection : IAggregateCommon, SingleProjections.ISingleProjection
        where TProjector : ISingleProjector<TProjection>, new()
    {
        var projector = new TProjector();
        var aggregate = projector.CreateInitialAggregate(aggregateId);
        var addFinished = false;
        await _documentRepository.GetAllEventsForAggregateIdAsync(
            aggregateId,
            projector.OriginalAggregateType(),
            PartitionKeyGenerator.ForEvent(aggregateId, projector.OriginalAggregateType()),
            null,
            events =>
            {
                if (events.Count() != events.Select(m => m.Id).Distinct().Count())
                {
                    throw new SekibanEventDuplicateException();
                }
                if (addFinished) { return; }
                foreach (var e in events)
                {
                    aggregate.ApplyEvent(e);
                    if (toVersion.HasValue && toVersion.Value == aggregate.Version)
                    {
                        addFinished = true;
                        break;
                    }
                }
            });
        if (aggregate.Version == 0) { return default; }
        return aggregate;
    }
}
