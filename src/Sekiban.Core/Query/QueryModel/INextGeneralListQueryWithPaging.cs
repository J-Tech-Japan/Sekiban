namespace Sekiban.Core.Query.QueryModel;

public interface INextGeneralListQueryWithPaging<TOutput> : INextGeneralListQuery<TOutput>, IQueryPagingParameterCommon where TOutput : notnull;
public interface ITenantNextGeneralListQueryWithPaging<TOutput> : INextGeneralListQuery<TOutput>, IQueryPagingParameterCommon, ITenantQueryCommon
    where TOutput : notnull;
