using FeatureCheck.Domain.Aggregates.Clients;
using FeatureCheck.Domain.Projections.ClientLoyaltyPointLists;
using ResultBoxes;
using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Projections;

public class GeneralListQuerySampleNext(string EmailContains) : INextGeneralListQueryAsync<GeneralListQuerySample_Response>
{
    public Task<ResultBox<IEnumerable<GeneralListQuerySample_Response>>> HandleFilterAsync(IQueryContext context) =>
        context.GetMultiProjectionService()
            .Conveyor(
                service => service.GetMultiProjectionWithResultAsync<ClientLoyaltyPointListProjection>()
                    .Combine(_ => service.GetAggregateListWithResult<Client>()))
            .Remap(
                (projectionA, clients) => projectionA.Payload.Records.Join(clients, x => x.ClientId, x => x.AggregateId, (x, y) => new { x, y })
                    .Where(x => x.y.Payload.ClientEmail.Contains(EmailContains))
                    .Select(x => new GeneralListQuerySample_Response(x.x.ClientName, x.x.BranchName)));
    public Task<ResultBox<IEnumerable<GeneralListQuerySample_Response>>> HandleSortAsync(
        IEnumerable<GeneralListQuerySample_Response> filteredList,
        IQueryContext context) =>
        ResultBox.WrapTry(() => filteredList.OrderBy(x => x.Name).AsEnumerable()).ToTask();
}