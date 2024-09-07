using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    INextSingleProjectionListWithPagingQuery<TSingleProjectionPayloadCommon, TQuery, TOutput> :
    INextSingleProjectionListQuery<TSingleProjectionPayloadCommon, TQuery, TOutput>,
    IQueryPagingParameterCommon where TOutput : notnull
    where TSingleProjectionPayloadCommon : ISingleProjectionPayloadCommon
    where TQuery : INextSingleProjectionListWithPagingQuery<TSingleProjectionPayloadCommon, TQuery, TOutput>;
