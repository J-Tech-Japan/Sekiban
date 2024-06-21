namespace Sekiban.Core.Query.QueryModel;

public interface ITenantNextGeneralListQueryAsync<TOutput> : INextGeneralListQueryAsync<TOutput>, ITenantQueryCommon where TOutput : notnull;
