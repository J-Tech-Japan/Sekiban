using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    INextMultiProjectionListWithPagingQuery<TMultiProjectionPayloadCommon, TQuery, TOutput> :
    INextMultiProjectionListQuery<TMultiProjectionPayloadCommon, TQuery, TOutput>,
    IQueryPagingParameterCommon where TOutput : notnull
    where TMultiProjectionPayloadCommon : IMultiProjectionPayloadCommon
    where TQuery : INextMultiProjectionListWithPagingQuery<TMultiProjectionPayloadCommon, TQuery, TOutput>,
    IEquatable<TQuery>;
