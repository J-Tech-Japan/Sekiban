using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Query.QueryModel;
using System.Data;
namespace FeatureCheck.Domain.Aggregates.Clients.Queries;

public record ClientEmailExistQueryNext(string Email) : INextAggregateQuery<Client, ClientEmailExistQueryNext, bool>
{
    public string RootPartitionKey { get; init; } = IDocument.DefaultRootPartitionKey;

    public string GetRootPartitionKey() => RootPartitionKey;

    public static ResultBox<bool> HandleFilter(
        IEnumerable<AggregateState<Client>> list,
        ClientEmailExistQueryNext query,
        IQueryContext context) =>
        ResultBox
            .Start
            .Verify(
                () => string.IsNullOrWhiteSpace(query.Email)
                    ? new NoNullAllowedException("Email should not be null")
                    : ExceptionOrNone.None)
            .Conveyor(() => ResultBox.WrapTry(() => list.Any(m => m.Payload.ClientEmail == query.Email)));
}
