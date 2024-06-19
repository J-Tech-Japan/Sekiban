namespace Sekiban.Core.Query.QueryModel;

public interface INextGeneralListQueryWithPagingAsync<TOutput> : INextGeneralListQueryAsync<TOutput>, IQueryPagingParameterCommon
    where TOutput : notnull;
public interface ITenantNextGeneralListQueryWithPagingAsync<TOutput> : INextGeneralListQueryAsync<TOutput>, IQueryPagingParameterCommon,
    ITenantQueryCommon where TOutput : notnull;
