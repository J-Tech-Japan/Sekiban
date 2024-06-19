using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface INextAggregateListQueryWithPaging<TAggregatePayload, TOutput> : INextAggregateListQuery<TAggregatePayload, TOutput>,
    IQueryPagingParameterCommon where TOutput : notnull where TAggregatePayload : IAggregatePayloadCommon;
public interface INextGeneralListQueryWithPaging<TOutput> : INextGeneralListQuery<TOutput>, IQueryPagingParameterCommon where TOutput : notnull;
