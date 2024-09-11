namespace Sekiban.Core.Query.QueryModel;

public interface ITenantNextGeneralQueryAsync<TQuery, TOutput> : INextGeneralQueryAsync<TQuery, TOutput>,
    ITenantQueryCommon where TOutput : notnull
    where TQuery : ITenantNextGeneralQueryAsync<TQuery, TOutput>, IEquatable<TQuery>
{
    string IQueryPartitionKeyCommon.GetRootPartitionKey() => GetTenantId();
}
