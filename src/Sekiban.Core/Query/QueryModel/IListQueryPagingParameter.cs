namespace Sekiban.Core.Query.QueryModel;

public interface IListQueryPagingParameter<TQueryOutput> : IListQueryParameter<TQueryOutput>, IQueryPagingParameterCommon
    where TQueryOutput : IQueryResponse
{
}
