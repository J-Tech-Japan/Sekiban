namespace Sekiban.Pure.Query;

public interface IListQueryCommon<TOutput> where TOutput : notnull;
public interface IListQueryCommon<TQuery, TOutput> : IListQueryCommon<TOutput>
    where TQuery : IListQueryCommon<TQuery, TOutput>, IEquatable<TQuery> where TOutput : notnull;