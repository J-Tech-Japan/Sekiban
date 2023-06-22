namespace Sekiban.Core.Query.QueryModel;

public interface IListQueryParameter<TQueryOutput> : IQueryParameterCommon, IListQueryInput<TQueryOutput> where TQueryOutput : IQueryResponse
{
}
