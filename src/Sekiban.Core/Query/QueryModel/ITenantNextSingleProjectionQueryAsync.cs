using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantNextSingleProjectionQueryAsync<TSingleProjectionPayloadCommon, TOutput> :
    INextSingleProjectionQueryAsync<TSingleProjectionPayloadCommon, TOutput>,
    ITenantQueryCommon where TOutput : notnull where TSingleProjectionPayloadCommon : ISingleProjectionPayloadCommon;
