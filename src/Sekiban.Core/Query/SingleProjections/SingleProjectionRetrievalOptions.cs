using Sekiban.Core.Command;
using Sekiban.Core.Documents.ValueObjects;
namespace Sekiban.Core.Query.SingleProjections;

public record SingleProjectionRetrievalOptions
{
    public int? PostponeEventFetchBySeconds { get; init; }
    public SortableUniqueIdValue? IncludesSortableUniqueIdValue { get; init; }
    public bool RetrieveNewEvents { get; init; } = true;
    public static SingleProjectionRetrievalOptions Default => new();
    public bool ShouldPostponeFetch(DateTime? cachedAt, DateTime? currentUtc)
    {
        if (cachedAt is null || currentUtc is null || PostponeEventFetchBySeconds is null)
        {
            return false;
        }
        var cachedAtPlusPostponedTime = cachedAt.Value.AddSeconds(PostponeEventFetchBySeconds.Value);
        return cachedAtPlusPostponedTime > currentUtc;
    }
    public static SingleProjectionRetrievalOptions WithPostponeFetchSeconds(int seconds) => new() { PostponeEventFetchBySeconds = seconds };
    public static SingleProjectionRetrievalOptions WithPostponeFetchMinutes(int minutes) => new() { PostponeEventFetchBySeconds = minutes * 60 };
    public static SingleProjectionRetrievalOptions WithPostponeFetchHours(int hours) => new() { PostponeEventFetchBySeconds = hours * 3600 };

    public static SingleProjectionRetrievalOptions? IncludeResultFromCommandResponse(CommandExecutorResponse commandResponse) =>
        commandResponse.LastSortableUniqueId is null
            ? null
            : new SingleProjectionRetrievalOptions { IncludesSortableUniqueIdValue = commandResponse.LastSortableUniqueId };
    public static SingleProjectionRetrievalOptions? IncludeResultFromCommandResponse(CommandExecutorResponseWithEvents commandResponse) =>
        commandResponse.LastSortableUniqueId is null
            ? null
            : new SingleProjectionRetrievalOptions { IncludesSortableUniqueIdValue = commandResponse.LastSortableUniqueId };
}
