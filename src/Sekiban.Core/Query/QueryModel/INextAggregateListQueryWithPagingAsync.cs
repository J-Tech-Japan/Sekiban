using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface INextAggregateListQueryWithPagingAsync<TAggregatePayload, TOutput> : INextAggregateListQueryAsync<TAggregatePayload, TOutput>,
    IQueryPagingParameterCommon where TOutput : notnull where TAggregatePayload : IAggregatePayloadCommon;