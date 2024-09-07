using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface
    INextAggregateListQueryWithPaging<TAggregatePayload, TQuery, TOutput> :
    INextAggregateListQuery<TAggregatePayload, TQuery, TOutput>,
    IQueryPagingParameterCommon where TOutput : notnull
    where TAggregatePayload : IAggregatePayloadCommon
    where TQuery : INextAggregateListQueryWithPaging<TAggregatePayload, TQuery, TOutput>;
