using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.MultiProjections.Projections;
using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Aggregates.Clients.Queries;

public class GetClientPayloadQuery : IAggregateListQuery<Client, GetClientPayloadQuery.Parameter, GetClientPayloadQuery.Response>
{
    public IEnumerable<Response> HandleFilter(Parameter queryParam, IEnumerable<AggregateState<Client>> list) =>
        list.Where(m => m.Payload.ClientName.Contains(queryParam.NameFilter)).Select(m => new Response(m.Payload, m.AggregateId, m.Version));
    public IEnumerable<Response> HandleSort(Parameter queryParam, IEnumerable<Response> filteredList) =>
        filteredList.OrderBy(m => m.Client.ClientName);
    public record Response(Client Client, Guid ClientId, int Version) : IQueryResponse;
    public record Parameter(string NameFilter) : IListQueryParameter<Response>, IQueryParameterMultiProjectionOptionSettable
    {
        // default value is null, but can add a options.
        public MultiProjectionRetrievalOptions? MultiProjectionRetrievalOptions { get; init; } = null;
    }
}
// public class TestingQueryOld(IQueryExecutor queryExecutor)
// {
//     public async Task TestHandle()
//     {
//         var result2 = await queryExecutor.ExecuteAsync(new GetClientPayloadQuery.Parameter("test@me.com"));
//     }
// }
public record GetClientPayloadQueryNext(string NameFilter) : INextAggregateListQuery<Client, GetClientPayloadQueryNext.Response>
{
    public ResultBox<IEnumerable<Response>> HandleFilter(IEnumerable<AggregateState<Client>> list, IQueryContext context) =>
        ResultBox.WrapTry(
            () => list.Where(m => m.Payload.ClientName.Contains(NameFilter)).Select(m => new Response(m.Payload, m.AggregateId, m.Version)));
    public ResultBox<IEnumerable<Response>> HandleSort(IEnumerable<Response> filteredList, IQueryContext context) =>
        ResultBox.WrapTry(() => filteredList.OrderBy(m => m.Client.ClientName).AsEnumerable());
    public record Response(Client Client, Guid ClientId, int Version);
}
public record GetClientPayloadQueryNextAsync(string NameFilter) : INextAggregateListQueryAsync<Client, GetClientPayloadQueryNextAsync.Response>
{
    public Task<ResultBox<IEnumerable<Response>>> HandleFilterAsync(IEnumerable<AggregateState<Client>> list, IQueryContext context) =>
        ResultBox.WrapTry(
                () => list.Where(m => m.Payload.ClientName.Contains(NameFilter)).Select(m => new Response(m.Payload, m.AggregateId, m.Version)))
            .ToTask();
    public Task<ResultBox<IEnumerable<Response>>> HandleSortAsync(IEnumerable<Response> filteredList, IQueryContext context) =>
        ResultBox.WrapTry(() => filteredList.OrderBy(m => m.Client.ClientName).AsEnumerable()).ToTask();
    public record Response(Client Client, Guid ClientId, int Version);
}
public record ClientEmailExistQueryNext(string Email) : INextAggregateQuery<Client, bool>
{
    public ResultBox<bool> HandleFilter(IEnumerable<AggregateState<Client>> list, IQueryContext context) =>
        ResultBox.WrapTry(() => list.Any(m => m.Payload.ClientEmail == Email));
}
// public class TestingQuery(IQueryExecutor queryExecutor)
// {
//     public async Task TestHandle()
//     {
//         var result = await queryExecutor.ExecuteNextAsync(new ClientEmailExistQueryNext("test@me.com"));
//         Console.WriteLine(result.IsSuccess ? $"Success{result.GetValue()}" : "Fail");
//         var result2 = await queryExecutor.ExecuteNextAsync(new GetClientPayloadQueryNext("test@me.com"));
//         Console.WriteLine(result2.IsSuccess ? "Success" : "Fail");
//         var result3 = await queryExecutor.ExecuteNextAsync(new GetClientPayloadQueryNextAsync("test@me.com"));
//         Console.WriteLine(result3.IsSuccess ? "Success" : "Fail");
//     }
// }
