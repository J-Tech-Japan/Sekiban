using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantNextMultiProjectionListWithPagingQuery<TMultiProjectionPayloadCommon, TOutput> :
    INextMultiProjectionListWithPagingQuery<TMultiProjectionPayloadCommon, TOutput>,
    ITenantQueryCommon where TOutput : notnull where TMultiProjectionPayloadCommon : IMultiProjectionPayloadCommon;
