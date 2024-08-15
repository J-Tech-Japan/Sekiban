namespace Sekiban.Core.Query.QueryModel;

public interface ITenantNextGeneralQuery<TOutput> : INextGeneralQuery<TOutput>, ITenantQueryCommon
    where TOutput : notnull;
