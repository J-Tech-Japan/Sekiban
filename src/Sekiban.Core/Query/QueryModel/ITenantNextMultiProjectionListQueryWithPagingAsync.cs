using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantNextMultiProjectionListQueryWithPagingAsync<TMultiProjectionPayloadCommon, TOutput> :
    INextMultiProjectionListQueryWithPagingAsync<TMultiProjectionPayloadCommon, TOutput>,
    ITenantQueryCommon where TOutput : notnull where TMultiProjectionPayloadCommon : IMultiProjectionPayloadCommon;
