using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantNextSingleProjectionListWithPagingQuery<TSingleProjectionPayloadCommon, TOutput> :
    INextSingleProjectionListQuery<TSingleProjectionPayloadCommon, TOutput>, IQueryPagingParameterCommon,
    ITenantQueryCommon where TOutput : notnull where TSingleProjectionPayloadCommon : ISingleProjectionPayloadCommon;