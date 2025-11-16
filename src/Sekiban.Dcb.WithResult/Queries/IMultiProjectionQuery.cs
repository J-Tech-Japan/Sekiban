using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.Queries;

/// <summary>
///     Alias for ICoreMultiProjectionQuery in WithResult package.
///     Interface for multi-projection queries that return a single result (ResultBox-based error handling).
/// </summary>
public interface IMultiProjectionQuery<TMultiProjector, TQuery, TOutput> : ICoreMultiProjectionQuery<TMultiProjector, TQuery, TOutput>
    where TMultiProjector : IMultiProjector<TMultiProjector>
    where TQuery : IMultiProjectionQuery<TMultiProjector, TQuery, TOutput>, IEquatable<TQuery>
    where TOutput : notnull
{
}
