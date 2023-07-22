using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Aggregates.Clients.Queries.BasicClientFilters;

public record BasicClientQueryParameter(
    Guid? BranchId,
    int? PageSize,
    int? PageNumber,
    BasicClientQuerySortKey? SortKey1,
    BasicClientQuerySortKey? SortKey2,
    bool? SortKey1Asc,
    bool? SortKey2Asc) : IListQueryPagingParameter<BasicClientQueryModel>;
