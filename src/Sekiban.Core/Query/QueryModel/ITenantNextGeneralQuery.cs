namespace Sekiban.Core.Query.QueryModel;

public interface ITenantNextGeneralQuery<TOutput> : INextGeneralQueryCommon<TOutput>, INextQueryCommon<TOutput>, ITenantQueryCommon
    where TOutput : notnull;