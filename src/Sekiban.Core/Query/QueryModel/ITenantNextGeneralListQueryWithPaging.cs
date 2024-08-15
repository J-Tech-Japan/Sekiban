namespace Sekiban.Core.Query.QueryModel;

public interface ITenantNextGeneralListQueryWithPaging<TOutput> : INextGeneralListQueryWithPaging<TOutput>,
    ITenantQueryCommon where TOutput : notnull;
