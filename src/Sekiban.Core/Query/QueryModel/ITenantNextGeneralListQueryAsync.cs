namespace Sekiban.Core.Query.QueryModel;

public interface ITenantNextGeneralListQueryAsync<TQuery, TOutput> : INextGeneralListQueryAsync<TQuery, TOutput>,
    ITenantQueryCommon where TOutput : notnull
    where TQuery : ITenantNextGeneralListQueryAsync<TQuery, TOutput>, IEquatable<TQuery>
{
    string IQueryPartitionKeyCommon.GetRootPartitionKey() => GetTenantId();
}
