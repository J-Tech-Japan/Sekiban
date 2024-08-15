using Sekiban.Core.Documents;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Partition;
namespace Sekiban.Core.Query.SingleProjections.Projections;

/// <summary>
///     Simple Single Projection From Initial Implementation.
/// </summary>
public class SimpleSingleProjectionFromInitial : ISingleProjectionFromInitial
{
    private readonly IDocumentRepository _documentRepository;

    public SimpleSingleProjectionFromInitial(IDocumentRepository documentRepository) =>
        _documentRepository = documentRepository;

    public async Task<TProjection?> GetAggregateFromInitialAsync<TProjection, TProjector>(
        Guid aggregateId,
        string rootPartitionKey,
        int? toVersion) where TProjection : IAggregateCommon, SingleProjections.ISingleProjection
        where TProjector : ISingleProjector<TProjection>, new()
    {
        var projector = new TProjector();
        var aggregate = projector.CreateInitialAggregate(aggregateId);
        var addFinished = false;
        await _documentRepository.GetAllEventsForAggregateIdAsync(
            aggregateId,
            projector.GetOriginalAggregatePayloadType(),
            PartitionKeyGenerator.ForEvent(aggregateId, projector.GetOriginalAggregatePayloadType(), rootPartitionKey),
            null,
            rootPartitionKey,
            events =>
            {
                var enumerable = events.ToList();
                if (enumerable.Count != enumerable.Select(m => m.Id).Distinct().Count())
                {
                    throw new SekibanEventDuplicateException();
                }
                if (addFinished)
                {
                    return;
                }
                foreach (var e in enumerable)
                {
                    aggregate.ApplyEvent(e);
                    if (toVersion.HasValue && toVersion.Value == aggregate.Version)
                    {
                        addFinished = true;
                        break;
                    }
                }
            });
        return aggregate.Version == 0 ? default : aggregate;
    }
}
