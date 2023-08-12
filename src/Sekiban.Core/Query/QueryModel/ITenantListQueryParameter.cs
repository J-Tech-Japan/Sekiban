namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     List Query Parameter for the Tenant Query.
///     Query developers can implement this interface directly.
/// </summary>
/// <typeparam name="TQueryOutput"></typeparam>
public interface ITenantListQueryParameter<TQueryOutput> : IListQueryParameter<TQueryOutput>, ITenantQueryCommon where TQueryOutput : IQueryResponse
{
    string IQueryPartitionKeyCommon.GetRootPartitionKey() => TenantId;
}
