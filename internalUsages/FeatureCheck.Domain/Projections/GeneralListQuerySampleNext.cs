using FeatureCheck.Domain.Aggregates.Clients;
using FeatureCheck.Domain.Projections.ClientLoyaltyPointLists;
using ResultBoxes;
using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Projections;

public record GeneralListQuerySampleNext(string EmailContains)
    : INextGeneralListQueryAsync<GeneralListQuerySampleNext, GeneralListQuerySample_Response>
{
    public static Task<ResultBox<IEnumerable<GeneralListQuerySample_Response>>> HandleFilterAsync(
        GeneralListQuerySampleNext query,
        IQueryContext context)
    {
        return context
            .GetMultiProjectionAsync<ClientLoyaltyPointListProjection>()
            .Combine(_ => context.GetAggregateList<Client>())
            .Remap(
                (projectionA, clients) => projectionA
                    .Payload
                    .Records
                    .Join(
                        clients,
                        x => x.ClientId,
                        x => x.AggregateId,
                        (x, y) => new
                        {
                            x, y
                        })
                    .Where(x => x.y.Payload.ClientEmail.Contains(query.EmailContains))
                    .Select(x => new GeneralListQuerySample_Response(x.x.ClientName, x.x.BranchName)));
    }

    public static Task<ResultBox<IEnumerable<GeneralListQuerySample_Response>>> HandleSortAsync(
        IEnumerable<GeneralListQuerySample_Response> filteredList,
        GeneralListQuerySampleNext query,
        IQueryContext context)
    {
        return ResultBox.WrapTry(() => filteredList.OrderBy(x => x.Name).AsEnumerable()).ToTask();
    }
}
