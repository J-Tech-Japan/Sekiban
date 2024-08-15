namespace Sekiban.Core.Query.QueryModel;

public interface INextGeneralListQueryWithPaging<TOutput> : INextGeneralListQuery<TOutput>, IQueryPagingParameterCommon
    where TOutput : notnull;
