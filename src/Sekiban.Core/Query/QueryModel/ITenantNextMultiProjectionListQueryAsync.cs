using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantNextMultiProjectionListQueryAsync<TMultiProjectionPayloadCommon, TOutput> :
    INextMultiProjectionQueryCommon<TMultiProjectionPayloadCommon, TOutput>, INextListQueryCommon<TOutput>,
    ITenantQueryCommon where TOutput : notnull where TMultiProjectionPayloadCommon : IMultiProjectionPayloadCommon;