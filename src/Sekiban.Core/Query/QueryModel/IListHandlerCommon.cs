using Sekiban.Core.Query.QueryModel.Parameters;
namespace Sekiban.Core.Query.QueryModel;

public interface IListHandlerCommon<in TQueryParameter, out TQueryResponse>
    where TQueryParameter : IQueryParameterCommon, IListQueryInput<TQueryResponse>
    where TQueryResponse : IQueryOutput
{
}
