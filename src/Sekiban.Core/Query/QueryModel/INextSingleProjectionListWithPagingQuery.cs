using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    INextSingleProjectionListWithPagingQuery<TSingleProjectionPayloadCommon, TOutput> :
    INextSingleProjectionListQuery<TSingleProjectionPayloadCommon, TOutput>,
    IQueryPagingParameterCommon where TOutput : notnull where TSingleProjectionPayloadCommon : ISingleProjectionPayloadCommon;