using Sekiban.Dcb.MultiProjections;
namespace Sekiban.Dcb.Queries;

/// <summary>
///     Alias for ICoreMultiProjectionListQuery in WithResult package.
///     Interface for multi-projection queries that return a list of results (ResultBox-based error handling).
/// </summary>
public interface IMultiProjectionListQuery<TMultiProjector, TQuery, TOutput> : ICoreMultiProjectionListQuery<TMultiProjector, TQuery, TOutput>
    where TMultiProjector : ICoreMultiProjector<TMultiProjector>
    where TQuery : IMultiProjectionListQuery<TMultiProjector, TQuery, TOutput>, IEquatable<TQuery>
    where TOutput : notnull
{
}
