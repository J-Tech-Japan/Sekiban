using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantNextSingleProjectionQuery<TSingleProjectionPayloadCommon, TOutput> : INextSingleProjectionQuery<TSingleProjectionPayloadCommon, TOutput>,
    ITenantQueryCommon where TOutput : notnull where TSingleProjectionPayloadCommon : ISingleProjectionPayloadCommon;
