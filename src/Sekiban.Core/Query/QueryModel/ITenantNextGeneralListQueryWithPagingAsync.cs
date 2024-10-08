namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantNextGeneralListQueryWithPagingAsync<TQuery, TOutput> : INextGeneralListQueryWithPagingAsync<TQuery, TOutput>,
    ITenantQueryCommon where TOutput : notnull
    where TQuery : ITenantNextGeneralListQueryWithPagingAsync<TQuery, TOutput>, IEquatable<TQuery>
{
    string IQueryPartitionKeyCommon.GetRootPartitionKey() => GetTenantId();
}
