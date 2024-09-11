using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface
    INextAggregateListQueryWithPagingAsync<TAggregatePayload, TQuery, TOutput> :
    INextAggregateListQueryAsync<TAggregatePayload, TQuery, TOutput>,
    IQueryPagingParameterCommon where TOutput : notnull
    where TAggregatePayload : IAggregatePayloadCommon
    where TQuery : INextAggregateListQueryWithPagingAsync<TAggregatePayload, TQuery, TOutput>, IEquatable<TQuery>;
