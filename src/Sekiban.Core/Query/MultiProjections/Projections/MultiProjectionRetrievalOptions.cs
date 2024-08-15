using Sekiban.Core.Command;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Query.QueryModel;
namespace Sekiban.Core.Query.MultiProjections.Projections;

public record MultiProjectionRetrievalOptions
{

    public int? PostponeEventFetchBySeconds { get; init; }
    public SortableUniqueIdValue? IncludesSortableUniqueIdValue { get; init; }
    public bool RetrieveNewEvents { get; init; } = true;
    public static MultiProjectionRetrievalOptions Default => new();

    public static MultiProjectionRetrievalOptions WithPostponeFetchSeconds(int seconds) =>
        new() { PostponeEventFetchBySeconds = seconds };
    public static MultiProjectionRetrievalOptions WithPostponeFetchMinutes(int minutes) =>
        new() { PostponeEventFetchBySeconds = minutes * 60 };
    public static MultiProjectionRetrievalOptions WithPostponeFetchHours(int hours) =>
        new() { PostponeEventFetchBySeconds = hours * 3600 };
    public bool ShouldPostponeFetch(DateTime? cachedAt, DateTime? currentUtc)
    {
        if (cachedAt is null || currentUtc is null || PostponeEventFetchBySeconds is null)
        {
            return false;
        }
        var cachedAtPlusPostponedTime = cachedAt.Value.AddSeconds(PostponeEventFetchBySeconds.Value);
        return cachedAtPlusPostponedTime > currentUtc;
    }
    public static MultiProjectionRetrievalOptions? IncludeResultFromCommandResponse(
        CommandExecutorResponse commandResponse) =>
        commandResponse.LastSortableUniqueId is null
            ? null
            : new MultiProjectionRetrievalOptions
                { IncludesSortableUniqueIdValue = commandResponse.LastSortableUniqueId };
    public static MultiProjectionRetrievalOptions? IncludeResultFromCommandResponse(
        CommandExecutorResponseWithEvents commandResponse) =>
        commandResponse.LastSortableUniqueId is null
            ? null
            : new MultiProjectionRetrievalOptions
                { IncludesSortableUniqueIdValue = commandResponse.LastSortableUniqueId };

    public static MultiProjectionRetrievalOptions? GetFromQuery(IQueryParameterCommon query) =>
        query is IQueryParameterMultiProjectionOptionSettable settable
            ? settable.MultiProjectionRetrievalOptions
            : null;
    public static MultiProjectionRetrievalOptions? GetFromQuery(INextQueryGeneral query) =>
        query is IQueryParameterMultiProjectionOptionSettable settable
            ? settable.MultiProjectionRetrievalOptions
            : null;
}
