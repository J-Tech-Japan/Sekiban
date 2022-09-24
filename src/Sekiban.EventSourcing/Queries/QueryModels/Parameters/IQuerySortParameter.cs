namespace Sekiban.EventSourcing.Queries.QueryModels.Parameters;

public interface IQuerySortParameter<TSortKey> : IQueryParameter where TSortKey : notnull
{
    public Dictionary<TSortKey, bool>? Sort { get; init; }
}
