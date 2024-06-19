namespace Sekiban.Core.Query.QueryModel;

public interface ITenantNextGeneralListQueryWithPaging<TOutput> : INextGeneralListQuery<TOutput>, IQueryPagingParameterCommon, ITenantQueryCommon
    where TOutput : notnull;