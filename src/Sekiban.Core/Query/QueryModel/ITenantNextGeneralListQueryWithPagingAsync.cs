namespace Sekiban.Core.Query.QueryModel;

public interface ITenantNextGeneralListQueryWithPagingAsync<TOutput> : INextGeneralListQueryWithPagingAsync<TOutput>,
    ITenantQueryCommon where TOutput : notnull
{
    string IQueryPartitionKeyCommon.GetRootPartitionKey() => GetTenantId();
}