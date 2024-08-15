using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    INextMultiProjectionListQueryWithPagingAsync<TMultiProjectionPayloadCommon, TOutput> :
    INextMultiProjectionListQueryAsync<TMultiProjectionPayloadCommon, TOutput>,
    IQueryPagingParameterCommon where TOutput : notnull
    where TMultiProjectionPayloadCommon : IMultiProjectionPayloadCommon;
