namespace Sekiban.Core.Query.QueryModel.Parameters;

public interface IQueryParameter<TQueryOutput> : IQueryParameterCommon, IQueryInput<TQueryOutput> where TQueryOutput : IQueryResponse
{
}
