namespace Sekiban.Core.Query.QueryModel;

public interface ITenantNextGeneralQueryAsync<TQuery, TOutput> : INextGeneralQueryAsync<TQuery, TOutput>,
    ITenantQueryCommon where TOutput : notnull where TQuery : ITenantNextGeneralQueryAsync<TQuery, TOutput>
{
    string IQueryPartitionKeyCommon.GetRootPartitionKey() => GetTenantId();
}
