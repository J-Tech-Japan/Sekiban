using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    INextSingleProjectionListQueryWithPagingAsync<TSingleProjectionPayloadCommon, TOutput> :
    INextSingleProjectionListQueryAsync<TSingleProjectionPayloadCommon, TOutput>,
    IQueryPagingParameterCommon where TOutput : notnull where TSingleProjectionPayloadCommon : ISingleProjectionPayloadCommon;
public interface
    ITenantNextSingleProjectionListQueryWithPagingAsync<TSingleProjectionPayloadCommon, TOutput> :
    INextSingleProjectionListQueryAsync<TSingleProjectionPayloadCommon, TOutput>, IQueryPagingParameterCommon,
    ITenantQueryCommon where TOutput : notnull where TSingleProjectionPayloadCommon : ISingleProjectionPayloadCommon;
