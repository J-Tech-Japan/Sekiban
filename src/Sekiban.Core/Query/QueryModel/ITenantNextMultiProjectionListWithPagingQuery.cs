using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantNextMultiProjectionListWithPagingQuery<TMultiProjectionPayloadCommon, TOutput> :
    INextMultiProjectionListQuery<TMultiProjectionPayloadCommon, TOutput>, IQueryPagingParameterCommon,
    ITenantQueryCommon where TOutput : notnull where TMultiProjectionPayloadCommon : IMultiProjectionPayloadCommon;