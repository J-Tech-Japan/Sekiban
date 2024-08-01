using FeatureCheck.Domain.Aggregates.Clients;
using FeatureCheck.Domain.Projections.ClientLoyaltyPointLists;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Projections;

public class GeneralQuerySample : IGeneralQuery<GeneralQuerySample.Parameter, GeneralQuerySample.Response>
{
    private readonly IMultiProjectionService _multiProjectionService;

    public GeneralQuerySample(IMultiProjectionService multiProjectionService) => _multiProjectionService = multiProjectionService;

    public async Task<Response> HandleFilterAsync(Parameter queryParam)
    {
        var projectionA = await _multiProjectionService.GetMultiProjectionAsync<ClientLoyaltyPointListProjection>();
        var clients = await _multiProjectionService.GetAggregateList<Client>();

        return new Response(
            projectionA.Payload.Records.Join(clients, x =>
                    x.ClientId, x => x.AggregateId, (x, y) => new
                {
                    x, y
                })
                .Count(x => x.y.Payload.ClientEmail.Contains(queryParam.EmailContains)));
    }

    public record Parameter(string EmailContains) : IQueryParameter<Response>;

    public record Response(int Count) : IQueryResponse;
}
