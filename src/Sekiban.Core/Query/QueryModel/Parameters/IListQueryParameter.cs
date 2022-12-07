namespace Sekiban.Core.Query.QueryModel.Parameters;

public interface IListQueryParameter<TQueryOutput> : IQueryParameterCommon, IListQueryInput<TQueryOutput> where TQueryOutput : IQueryOutput
{
}
