using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface INextAggregateListQueryWithPagingAsync<TAggregatePayload, TOutput> : INextAggregateListQueryAsync<TAggregatePayload, TOutput>,
    IQueryPagingParameterCommon where TOutput : notnull where TAggregatePayload : IAggregatePayloadCommon;
public interface INextGeneralListQueryWithPagingAsync<TOutput> : INextGeneralListQueryAsync<TOutput>, IQueryPagingParameterCommon
    where TOutput : notnull;
