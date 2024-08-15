namespace Sekiban.Core.Query.QueryModel;

public interface ITenantNextGeneralQueryAsync<TOutput> : INextGeneralQueryAsync<TOutput>, ITenantQueryCommon
    where TOutput : notnull
{
    string IQueryPartitionKeyCommon.GetRootPartitionKey() => GetTenantId();
}
