namespace Sekiban.Core.Query.QueryModel;

public interface ITenantQueryParameter<TQueryOutput> : IQueryParameter<TQueryOutput>, ITenantQueryCommon where TQueryOutput : IQueryResponse
{
    string IQueryPartitionKeyCommon.GetRootPartitionKey() => TenantId;
}
