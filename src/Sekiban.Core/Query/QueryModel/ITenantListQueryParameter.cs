namespace Sekiban.Core.Query.QueryModel;

public interface ITenantListQueryParameter<TQueryOutput> : IListQueryParameter<TQueryOutput>, ITenantQueryCommon where TQueryOutput : IQueryResponse
{
    string IQueryPartitionKeyCommon.GetRootPartitionKey() => TenantId;
}
