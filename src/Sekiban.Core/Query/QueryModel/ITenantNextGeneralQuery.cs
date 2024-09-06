namespace Sekiban.Core.Query.QueryModel;

public interface ITenantNextGeneralQuery<TQuery, TOutput> : INextGeneralQuery<TQuery, TOutput>, ITenantQueryCommon
    where TOutput : notnull where TQuery : ITenantNextGeneralQuery<TQuery, TOutput>
{
    string IQueryPartitionKeyCommon.GetRootPartitionKey() => GetTenantId();
}
