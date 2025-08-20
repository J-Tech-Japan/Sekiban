namespace Sekiban.Dcb.Queries;

/// <summary>
/// Base interface for all queries
/// </summary>
public interface IQueryCommon;

/// <summary>
/// Interface for queries with a specific output type
/// </summary>
public interface IQueryCommon<TOutput> : IQueryCommon 
    where TOutput : notnull;

/// <summary>
/// Interface for strongly-typed queries
/// </summary>
public interface IQueryCommon<TQuery, TOutput> : IQueryCommon<TOutput> 
    where TOutput : notnull
    where TQuery : IQueryCommon<TQuery, TOutput>, IEquatable<TQuery>;