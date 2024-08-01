using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Query.QueryModel;
using System.Data;
namespace FeatureCheck.Domain.Aggregates.Clients.Queries;

public record ClientEmailExistQueryN(string Email) : INextAggregateQuery<Client, bool>
{
    public string RootPartitionKey { get; init; } = IDocument.DefaultRootPartitionKey;

    public string GetRootPartitionKey() => RootPartitionKey;

    public ResultBox<bool> HandleFilter(IEnumerable<AggregateState<Client>> list, IQueryContext context)
    {
        return ResultBox
            .Start
            .Verify(
                () => string.IsNullOrWhiteSpace(Email)
                    ? new NoNullAllowedException("Email should not be null")
                    : ExceptionOrNone.None)
            .Conveyor(() => ResultBox.WrapTry(() => list.Any(m => m.Payload.ClientEmail == Email)));
    }
}
