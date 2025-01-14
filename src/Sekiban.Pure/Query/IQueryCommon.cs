namespace Sekiban.Pure.Query;

public interface IQueryCommon<TOutput> where TOutput : notnull;
public interface IQueryCommon<TQuery, TOutput> where TOutput : notnull
    where TQuery : IQueryCommon<TQuery, TOutput>, IEquatable<TQuery>;
