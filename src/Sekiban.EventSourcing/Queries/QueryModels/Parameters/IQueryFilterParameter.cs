namespace Sekiban.EventSourcing.Queries.QueryModels.Parameters;

public interface IQueryFilterParameter<TSortKey> : IQueryParameter, IQuerySortParameter<TSortKey>, IQueryPagingParameter where TSortKey : struct
{
}
