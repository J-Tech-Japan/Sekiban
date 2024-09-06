using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
using System.Data;
namespace FeatureCheck.Domain.Aggregates.Clients.Queries;

public record GetClientPayloadQueryNext(string NameFilter)
    : INextAggregateListQuery<Client, GetClientPayloadQueryNext, GetClientPayloadQuery_Response>
{
    public static ResultBox<IEnumerable<GetClientPayloadQuery_Response>> HandleFilter(
        IEnumerable<AggregateState<Client>> list,
        GetClientPayloadQueryNext query,
        IQueryContext context) => ResultBox
        .Start
        .Verify(
            () => string.IsNullOrWhiteSpace(query.NameFilter)
                ? new NoNullAllowedException("NameFilter")
                : ExceptionOrNone.None)
        .Conveyor(
            () => ResultBox.WrapTry(
                () => list
                    .Where(m => m.Payload.ClientName.Contains(query.NameFilter))
                    .Select(m => new GetClientPayloadQuery_Response(m.Payload, m.AggregateId, m.Version))));
    public static ResultBox<IEnumerable<GetClientPayloadQuery_Response>> HandleSort(
        IEnumerable<GetClientPayloadQuery_Response> filteredList,
        GetClientPayloadQueryNext query,
        IQueryContext context) =>
        ResultBox.WrapTry(() => filteredList.OrderBy(m => m.Client.ClientName).AsEnumerable());
}
