namespace Sekiban.Dcb.Queries;

/// <summary>
/// Base interface for all list queries
/// </summary>
public interface IListQueryCommon;

/// <summary>
/// Interface for list queries with a specific output type
/// </summary>
public interface IListQueryCommon<TOutput> : IListQueryCommon 
    where TOutput : notnull;

/// <summary>
/// Interface for strongly-typed list queries
/// </summary>
public interface IListQueryCommon<TQuery, TOutput> : IListQueryCommon<TOutput>
    where TQuery : IListQueryCommon<TQuery, TOutput>, IEquatable<TQuery> 
    where TOutput : notnull;