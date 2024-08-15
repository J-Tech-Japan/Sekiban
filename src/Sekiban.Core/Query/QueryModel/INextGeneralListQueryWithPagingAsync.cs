namespace Sekiban.Core.Query.QueryModel;

public interface INextGeneralListQueryWithPagingAsync<TOutput> : INextGeneralListQueryAsync<TOutput>,
    IQueryPagingParameterCommon where TOutput : notnull;
