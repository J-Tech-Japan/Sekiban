using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Query.QueryModel;
namespace Sekiban.Core.Query.MultiProjections.Projections;

public record MultiProjectionRetrievalOptions
{
    public SortableUniqueIdValue? IncludesSortableUniqueIdValue { get; init; }
    public bool RetrieveNewEvents { get; init; } = true;
    public static MultiProjectionRetrievalOptions Default => new();
    public static MultiProjectionRetrievalOptions? GetFromQuery(IQueryParameterCommon query) =>
        query is IQueryParameterMultiProjectionOptionSettable settable ? settable.MultiProjectionRetrievalOptions : null;
}
