namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     Tenant Query Parameter Interface.
///     Query developers can implement this interface directly.
/// </summary>
/// <typeparam name="TQueryOutput"></typeparam>
public interface ITenantQueryParameter<TQueryOutput> : IQueryParameter<TQueryOutput>, ITenantQueryCommon where TQueryOutput : IQueryResponse
{
    string IQueryPartitionKeyCommon.GetRootPartitionKey() => TenantId;
}
