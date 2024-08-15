namespace Sekiban.Core.Query.QueryModel;

public interface ITenantNextGeneralListQuery<TOutput> : INextGeneralListQuery<TOutput>, ITenantQueryCommon
    where TOutput : notnull
{
    string IQueryPartitionKeyCommon.GetRootPartitionKey() => GetTenantId();
}

