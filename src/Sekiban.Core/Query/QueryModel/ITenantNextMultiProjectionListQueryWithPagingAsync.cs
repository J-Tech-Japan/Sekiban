using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantNextMultiProjectionListQueryWithPagingAsync<TMultiProjectionPayloadCommon, TOutput> :
    INextMultiProjectionListQueryAsync<TMultiProjectionPayloadCommon, TOutput>, IQueryPagingParameterCommon,
    ITenantQueryCommon where TOutput : notnull where TMultiProjectionPayloadCommon : IMultiProjectionPayloadCommon;