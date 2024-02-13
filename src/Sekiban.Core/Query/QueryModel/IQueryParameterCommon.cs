using Sekiban.Core.Query.MultiProjections.Projections;
namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     Query Parameter Interface.
///     Query developers does not need to implement this interface directly.
/// </summary>
public interface IQueryParameterCommon
{
}
public interface IQueryParameterMultiProjectionOptionSettable
{
    public MultiProjectionRetrievalOptions? MultiProjectionRetrievalOptions { get; }
}
