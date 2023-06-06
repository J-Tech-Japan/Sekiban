using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
namespace FeatureCheck.Domain.Projections.ClientLoyaltyPointMultiples;

public class ClientLoyaltyPointExceptionTestQuery : IMultiProjectionQuery<ClientLoyaltyPointMultiProjection,
    ClientLoyaltyPointExceptionTestQuery.Parameter, ClientLoyaltyPointExceptionTestQuery.Response>
{

    public Response HandleFilter(Parameter queryParam, MultiProjectionState<ClientLoyaltyPointMultiProjection> projection) =>
        queryParam.Param switch
        {
            0 => new Response(true),
            1 => new Response(false),
            2 => throw new InvalidDataException("Invalid query parameter"),
            _ => throw new NotImplementedException()
        };
    public record Parameter(int Param) : IQueryParameter<Response>;
    public record Response(bool TestResult) : IQueryResponse;
}
