using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface ITenantNextAggregateListQueryWithPagingAsync<TAggregatePayload, TOutput> : INextAggregateListQueryAsync<TAggregatePayload, TOutput>,
    IQueryPagingParameterCommon, ITenantQueryCommon where TOutput : notnull where TAggregatePayload : IAggregatePayloadCommon;