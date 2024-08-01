using FeatureCheck.Domain.Aggregates.Clients;
using FeatureCheck.Domain.Projections.ClientLoyaltyPointLists;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Projections;

public class
    GeneralListQuerySample : IGeneralListQuery<GeneralListQuerySample.Parameter, GeneralListQuerySample_Response>
{
    private readonly IMultiProjectionService _multiProjectionService;

    public GeneralListQuerySample(IMultiProjectionService multiProjectionService) => _multiProjectionService = multiProjectionService;

    public async Task<IEnumerable<GeneralListQuerySample_Response>> HandleFilterAsync(Parameter queryParam)
    {
        var projectionA = await _multiProjectionService.GetMultiProjectionAsync<ClientLoyaltyPointListProjection>();
        var clients = await _multiProjectionService.GetAggregateList<Client>();

        return projectionA.Payload.Records.Join(clients, x => x.ClientId, x => x.AggregateId, (x, y) => new
            {
                x, y
            })
            .Where(x => x.y.Payload.ClientEmail.Contains(queryParam.EmailContains))
            .Select(x => new GeneralListQuerySample_Response(x.x.ClientName, x.x.BranchName));
    }

    public IEnumerable<GeneralListQuerySample_Response> HandleSort(Parameter queryParam,
        IEnumerable<GeneralListQuerySample_Response> filteredList)
    {
        return filteredList.OrderBy(x => x.Name);
    }

    public record Parameter(string EmailContains) : IListQueryParameter<GeneralListQuerySample_Response>;
}
