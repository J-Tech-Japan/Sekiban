using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
using System.Data;
namespace FeatureCheck.Domain.Aggregates.Clients.Queries;

public record GetClientPayloadQueryNext(string NameFilter)
    : INextAggregateListQuery<Client, GetClientPayloadQueryNext, GetClientPayloadQuery_Response>
{
    public ResultBox<IEnumerable<GetClientPayloadQuery_Response>> HandleFilter(
        IEnumerable<AggregateState<Client>> list,
        IQueryContext context)
    {
        return ResultBox
            .Start
            .Verify(
                () => string.IsNullOrWhiteSpace(NameFilter)
                    ? new NoNullAllowedException("NameFilter")
                    : ExceptionOrNone.None)
            .Conveyor(
                () => ResultBox.WrapTry(
                    () => list
                        .Where(m => m.Payload.ClientName.Contains(NameFilter))
                        .Select(m => new GetClientPayloadQuery_Response(m.Payload, m.AggregateId, m.Version))));
    }

    public ResultBox<IEnumerable<GetClientPayloadQuery_Response>> HandleSort(
        IEnumerable<GetClientPayloadQuery_Response> filteredList,
        IQueryContext context)
    {
        return ResultBox.WrapTry(() => filteredList.OrderBy(m => m.Client.ClientName).AsEnumerable());
    }
}
