namespace Sekiban.Core.Documents;

/// <summary>
///     Document repository that can be cleared every time server restarts
///     use fot the in memory document store.
/// </summary>
public interface IDocumentTemporaryRepository : IDocumentRepository
{
    /// <summary>
    ///     returns in memory store has data with valid Sortable Unique Id
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="originalType"></param>
    /// <param name="partitionKey"></param>
    /// <param name="sortableUniqueId"></param>
    /// <returns></returns>
    Task<bool> EventsForAggregateIdHasSortableUniqueIdAsync(
        Guid aggregateId,
        Type originalType,
        string? partitionKey,
        string? sortableUniqueId);
}
