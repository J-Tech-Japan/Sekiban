using Sekiban.Core.Query.MultiProjections.Projections;
namespace Sekiban.Core.Query.QueryModel;

public interface IQueryParameterMultiProjectionOptionSettable
{
    public MultiProjectionRetrievalOptions? MultiProjectionRetrievalOptions { get; }
}
