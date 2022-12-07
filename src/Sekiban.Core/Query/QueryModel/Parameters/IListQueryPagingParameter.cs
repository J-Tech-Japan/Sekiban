namespace Sekiban.Core.Query.QueryModel.Parameters;

public interface IListQueryPagingParameter<TQueryOutput> : IListQueryParameter<TQueryOutput> where TQueryOutput : IQueryOutput
{
}
