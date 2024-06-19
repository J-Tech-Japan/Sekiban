using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface ITenantNextAggregateListQueryWithPaging<TAggregatePayload, TOutput> : INextAggregateListQuery<TAggregatePayload, TOutput>,
    IQueryPagingParameterCommon, ITenantQueryCommon where TOutput : notnull where TAggregatePayload : IAggregatePayloadCommon;