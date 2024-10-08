namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantNextGeneralListQueryWithPaging<TQuery, TOutput> : INextGeneralListQueryWithPaging<TQuery, TOutput>,
    ITenantQueryCommon where TOutput : notnull
    where TQuery : ITenantNextGeneralListQueryWithPaging<TQuery, TOutput>, IEquatable<TQuery>
{
    string IQueryPartitionKeyCommon.GetRootPartitionKey() => GetTenantId();
}
