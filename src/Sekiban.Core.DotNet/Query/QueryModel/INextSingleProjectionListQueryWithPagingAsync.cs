using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    INextSingleProjectionListQueryWithPagingAsync<TSingleProjectionPayloadCommon, TQuery, TOutput> :
    INextSingleProjectionListQueryAsync<TSingleProjectionPayloadCommon, TQuery, TOutput>,
    IQueryPagingParameterCommon where TOutput : notnull
    where TSingleProjectionPayloadCommon : ISingleProjectionPayloadCommon
    where TQuery : INextSingleProjectionListQueryWithPagingAsync<TSingleProjectionPayloadCommon, TQuery, TOutput>,
    IEquatable<TQuery>;
