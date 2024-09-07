using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Aggregates.Clients.Queries;

public record ClientEmailExistQueryNextAsync(string Email)
    : INextAggregateQueryAsync<Client, ClientEmailExistQueryNextAsync, bool>
{
    public static Task<ResultBox<bool>> HandleFilterAsync(
        IEnumerable<AggregateState<Client>> list,
        ClientEmailExistQueryNextAsync query,
        IQueryContext context) =>
        ResultBox.WrapTry(() => list.Any(m => m.Payload.ClientEmail == query.Email)).ToTask();
}
