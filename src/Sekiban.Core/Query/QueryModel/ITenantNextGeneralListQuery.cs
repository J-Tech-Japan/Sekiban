namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantNextGeneralListQuery<TQuery, TOutput> : INextGeneralListQuery<TQuery, TOutput>, ITenantQueryCommon
    where TOutput : notnull where TQuery : ITenantNextGeneralListQuery<TQuery, TOutput>, IEquatable<TQuery>
{
    string IQueryPartitionKeyCommon.GetRootPartitionKey() => GetTenantId();
}
