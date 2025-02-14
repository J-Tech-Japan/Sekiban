namespace Sekiban.Pure.Query;

public interface IQueryCommon<TOutput> : IQueryCommon where TOutput : notnull;
public interface IQueryCommon<TQuery, TOutput> : IQueryCommon<TOutput> where TOutput : notnull
    where TQuery : IQueryCommon<TQuery, TOutput>, IEquatable<TQuery>;
public interface IQueryCommon;