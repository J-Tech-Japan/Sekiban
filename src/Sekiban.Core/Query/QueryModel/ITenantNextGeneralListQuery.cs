namespace Sekiban.Core.Query.QueryModel;

public interface ITenantNextGeneralListQuery<TOutput> : INextGeneralQueryCommon<TOutput>, INextListQueryCommon<TOutput>, ITenantQueryCommon
    where TOutput : notnull;