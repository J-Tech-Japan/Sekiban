using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Aggregates.Clients.Queries;

public record ClientEmailExistQueryNext(string Email) : INextAggregateQuery<Client, bool>
{
    public ResultBox<bool> HandleFilter(IEnumerable<AggregateState<Client>> list, IQueryContext context) =>
        ResultBox.WrapTry(() => list.Any(m => m.Payload.ClientEmail == Email));
}