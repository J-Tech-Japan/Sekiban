using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantNextSingleProjectionQuery<TSingleProjectionPayloadCommon, TQuery, TOutput> :
    INextSingleProjectionQuery<TSingleProjectionPayloadCommon, TQuery, TOutput>,
    ITenantQueryCommon where TOutput : notnull
    where TSingleProjectionPayloadCommon : ISingleProjectionPayloadCommon
    where TQuery : ITenantNextSingleProjectionQuery<TSingleProjectionPayloadCommon, TQuery, TOutput>
{
    string IQueryPartitionKeyCommon.GetRootPartitionKey() => GetTenantId();
}
