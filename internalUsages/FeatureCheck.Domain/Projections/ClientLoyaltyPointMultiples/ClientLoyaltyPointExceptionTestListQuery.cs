using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Projections.ClientLoyaltyPointMultiples;

public class ClientLoyaltyPointExceptionTestListQuery : IMultiProjectionListQuery<ClientLoyaltyPointMultiProjection,
    ClientLoyaltyPointExceptionTestListQuery.Parameter, ClientLoyaltyPointExceptionTestListQuery.Response>
{
    public IEnumerable<Response> HandleSort(Parameter queryParam, IEnumerable<Response> filteredList)
    {
        return filteredList.OrderBy(m => m.TestResult);
    }

    IEnumerable<Response> IMultiProjectionListQuery<ClientLoyaltyPointMultiProjection, Parameter, Response>.
        HandleFilter(Parameter queryParam, MultiProjectionState<ClientLoyaltyPointMultiProjection> projection)
    {
        return queryParam.Param switch
        {
            0 => [new Response(true)],
            1 => [new Response(false)],
            2 => throw new InvalidDataException("Invalid query parameter"),
            _ => throw new NotImplementedException()
        };
    }

    public record Parameter(int Param) : IListQueryParameter<Response>;

    public record Response(bool TestResult) : IQueryResponse;
}
