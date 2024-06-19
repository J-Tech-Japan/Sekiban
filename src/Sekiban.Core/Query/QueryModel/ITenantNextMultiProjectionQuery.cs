using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantNextMultiProjectionQuery<TMultiProjectionPayloadCommon, TOutput> : INextMultiProjectionQueryCommon<TMultiProjectionPayloadCommon, TOutput>,
    INextQueryCommon<TOutput>, ITenantQueryCommon where TOutput : notnull where TMultiProjectionPayloadCommon : IMultiProjectionPayloadCommon;