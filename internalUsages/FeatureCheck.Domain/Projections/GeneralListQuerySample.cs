using FeatureCheck.Domain.Aggregates.Clients;
using FeatureCheck.Domain.Projections.ClientLoyaltyPointLists;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Projections;

public class GeneralListQuerySample : IGeneralListQuery<GeneralListQuerySample.Parameter, GeneralListQuerySample.Response>
{
    private readonly IMultiProjectionService _multiProjectionService;

    public GeneralListQuerySample(IMultiProjectionService multiProjectionService) => _multiProjectionService = multiProjectionService;
    public async Task<IEnumerable<Response>> HandleFilterAsync(Parameter queryParam)
    {
        var projectionA = await _multiProjectionService.GetMultiProjectionAsync<ClientLoyaltyPointListProjection>();
        var clients = await _multiProjectionService.GetAggregateList<Client>();

        return projectionA.Payload.Records.Join(clients, x => x.ClientId, x => x.AggregateId, (x, y) => new { x, y })
            .Where(x => x.y.Payload.ClientEmail.Contains(queryParam.EmailContains))
            .Select(x => new Response(x.x.ClientName, x.x.BranchName));
    }
    public IEnumerable<Response> HandleSort(Parameter queryParam, IEnumerable<Response> filteredList) => filteredList.OrderBy(x => x.Name);

    public record Parameter(string EmailContains) : IListQueryParameter<Response>;
    public record Response(string Name, string BranchName) : IQueryResponse;
}
