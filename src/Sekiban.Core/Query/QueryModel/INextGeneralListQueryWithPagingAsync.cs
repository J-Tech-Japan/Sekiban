namespace Sekiban.Core.Query.QueryModel;

public interface INextGeneralListQueryWithPagingAsync<TQuery, TOutput> : INextGeneralListQueryAsync<TQuery, TOutput>,
    IQueryPagingParameterCommon where TOutput : notnull
    where TQuery : INextGeneralListQueryWithPagingAsync<TQuery, TOutput>;
