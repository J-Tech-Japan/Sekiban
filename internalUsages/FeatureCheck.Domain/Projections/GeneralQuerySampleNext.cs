using FeatureCheck.Domain.Aggregates.Clients;
using FeatureCheck.Domain.Projections.ClientLoyaltyPointLists;
using ResultBoxes;
using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Projections;

public record GeneralQuerySampleNext(string EmailContains) : INextGeneralQueryAsync<int>
{
    public Task<ResultBox<int>> HandleFilterAsync(IQueryContext context)
    {
        return context.GetMultiProjectionAsync<ClientLoyaltyPointListProjection>()
            .Combine(_ => context.GetAggregateList<Client>())
            .Remap(
                (projectionA, clients) => projectionA.Payload.Records.Join(clients, x => x.ClientId, x => x.AggregateId,
                        (x, y) => new
                        {
                            x, y
                        })
                    .Count(x => x.y.Payload.ClientEmail.Contains(EmailContains)));
    }
}
