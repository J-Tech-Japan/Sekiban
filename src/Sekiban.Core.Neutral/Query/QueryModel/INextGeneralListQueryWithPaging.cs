namespace Sekiban.Core.Query.QueryModel;

public interface INextGeneralListQueryWithPaging<TQuery, TOutput> : INextGeneralListQuery<TQuery, TOutput>,
    IQueryPagingParameterCommon where TOutput : notnull
    where TQuery : INextGeneralListQueryWithPaging<TQuery, TOutput>, IEquatable<TQuery>;
