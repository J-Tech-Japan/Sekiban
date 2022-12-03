using Sekiban.Core.Query.QueryModel.Parameters;
namespace Sekiban.Core.Query.QueryModel;

public interface IListQueryCommon<in TQueryParameter, out TQueryResponse>
    where TQueryParameter : IQueryParameter, IQueryInput<TQueryResponse>
    where TQueryResponse : IQueryOutput
{
}
