namespace Sekiban.Core.Query.QueryModel;

public interface ITenantNextGeneralListQueryWithPagingAsync<TOutput> : INextGeneralListQueryAsync<TOutput>, IQueryPagingParameterCommon,
    ITenantQueryCommon where TOutput : notnull;