using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantNextSingleProjectionListQueryAsync<TSingleProjectionPayloadCommon, TOutput> :
    INextSingleProjectionListQueryAsync<TSingleProjectionPayloadCommon, TOutput>,
    ITenantQueryCommon where TOutput : notnull where TSingleProjectionPayloadCommon : ISingleProjectionPayloadCommon;
