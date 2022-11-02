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
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="P"></typeparam>
    /// <returns></returns>
    public async Task<T?> GetAggregateFromInitialAsync<T, P>(Guid aggregateId, int? toVersion)
        where T : IAggregateCommon, SingleProjections.ISingleProjection
        where P : ISingleProjector<T>, new()
    {
        var projector = new P();
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
