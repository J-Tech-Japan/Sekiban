using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    INextMultiProjectionListQueryWithPagingAsync<TMultiProjectionPayloadCommon, TQuery, TOutput> :
    INextMultiProjectionListQueryAsync<TMultiProjectionPayloadCommon, TQuery, TOutput>,
    IQueryPagingParameterCommon where TOutput : notnull
    where TMultiProjectionPayloadCommon : IMultiProjectionPayloadCommon
    where TQuery : INextMultiProjectionListQueryWithPagingAsync<TMultiProjectionPayloadCommon, TQuery, TOutput>;
