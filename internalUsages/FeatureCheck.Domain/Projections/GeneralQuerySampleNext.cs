using FeatureCheck.Domain.Aggregates.Clients;
using FeatureCheck.Domain.Projections.ClientLoyaltyPointLists;
using ResultBoxes;
using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Projections;

public class GeneralQuerySampleNext(string EmailContains) : INextGeneralQueryAsync<int>
{
    public Task<ResultBox<int>> HandleFilterAsync(IQueryContext context) =>
        context.GetMultiProjectionService()
            .Conveyor(
                service => service.GetMultiProjectionWithResultAsync<ClientLoyaltyPointListProjection>()
                    .Combine(_ => service.GetAggregateListWithResult<Client>()))
            .Remap(
                (projectionA, clients) => projectionA.Payload.Records.Join(clients, x => x.ClientId, x => x.AggregateId, (x, y) => new { x, y })
                    .Count(x => x.y.Payload.ClientEmail.Contains(EmailContains)));
}