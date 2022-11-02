using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Shared;
using System.Text.Json;
using Xunit;
namespace Sekiban.Testing.Queries;

public class
    SingleProjectionListQueryTestChecker<TAggregatePayload, TSingleProjection, TSingleProjectionPayload, TQuery,
        TQueryParameter, TQueryResponse> : IQueryChecker
    where TAggregatePayload : IAggregatePayload, new()
    where TSingleProjection : SingleProjectionBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>, new
    ()
    where TSingleProjectionPayload : ISingleProjectionPayload
    where TQuery : ISingleProjectionListQuery<TAggregatePayload, TSingleProjection,
        TSingleProjectionPayload, TQueryParameter, TQueryResponse>
    where TQueryParameter : IQueryParameter
{
    public QueryListResult<TQueryResponse>? Response { get; set; }
    public IQueryService? QueryService { get; set; } = null;
    public SingleProjectionListQueryTestChecker<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TQuery, TQueryParameter, TQueryResponse> WhenParam(TQueryParameter param)
    {
        if (QueryService == null) { throw new MissingMemberException(nameof(QueryService)); }
        Response = QueryService
            .GetSingleProjectionListQueryAsync<TAggregatePayload, TSingleProjection, TSingleProjectionPayload, TQuery, TQueryParameter,
                TQueryResponse>(param)
            .Result;
        return this;
    }
    public SingleProjectionListQueryTestChecker<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TQuery, TQueryParameter, TQueryResponse> WriteResponseToFile(string filename)
    {
        if (Response == null)
        {
            throw new InvalidDataException("Response is null");
        }
        var json = SekibanJsonHelper.Serialize(Response);
        if (string.IsNullOrEmpty(json))
        {
            throw new InvalidDataException("Json is null or empty");
        }
        File.WriteAllTextAsync(filename, json);
        return this;
    }
    public SingleProjectionListQueryTestChecker<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TQuery, TQueryParameter, TQueryResponse> ThenResponseIs(QueryListResult<TQueryResponse> expectedResponse)
    {
        if (Response == null)
        {
            throw new InvalidDataException("Response is null");
        }
        var actual = Response;
        var expected = expectedResponse;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public SingleProjectionListQueryTestChecker<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TQuery, TQueryParameter, TQueryResponse> ThenGetResponse(Action<QueryListResult<TQueryResponse>> responseAction)
    {
        Assert.NotNull(Response);
        responseAction(Response!);
        return this;
    }

    public SingleProjectionListQueryTestChecker<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TQuery, TQueryParameter, TQueryResponse> ThenResponseIsFromJson(string responseJson)
    {
        var response = JsonSerializer.Deserialize<QueryListResult<TQueryResponse>>(responseJson);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenResponseIs(response);
        return this;
    }
    public SingleProjectionListQueryTestChecker<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TQuery, TQueryParameter, TQueryResponse> ThenResponseIsFromFile(string responseFilename)
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<QueryListResult<TQueryResponse>>(openStream);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenResponseIs(response);
        return this;
    }
}
