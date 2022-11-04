using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Shared;
using System.Text.Json;
using Xunit;
namespace Sekiban.Testing.Queries;

public class SingleProjectionQueryTest<TAggregatePayload, TSingleProjection, TSingleProjectionPayload, TQuery,
    TQueryParameter, TQueryResponse> : IQueryTest
    where TAggregatePayload : IAggregatePayload, new()
    where TSingleProjection : SingleProjectionBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>, new
    ()
    where TSingleProjectionPayload : ISingleProjectionPayload
    where TQuery : ISingleProjectionQuery<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TQueryParameter, TQueryResponse>
    where TQueryParameter : IQueryParameter
{
    public TQueryResponse? Response { get; set; }
    public IQueryService? QueryService { get; set; } = null;
    public SingleProjectionQueryTest<TAggregatePayload, TSingleProjection, TSingleProjectionPayload
        , TQuery, TQueryParameter, TQueryResponse> WhenParam(TQueryParameter param)
    {
        if (QueryService == null) { throw new MissingMemberException(nameof(QueryService)); }
        Response = QueryService
            .GetSingleProjectionQueryAsync<TAggregatePayload, TSingleProjection, TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
                param)
            .Result;

        return this;
    }
    public SingleProjectionQueryTest<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TQuery, TQueryParameter, TQueryResponse> WriteResponse(string filename)
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
    public SingleProjectionQueryTest<TAggregatePayload, TSingleProjection, TSingleProjectionPayload, TQuery,
        TQueryParameter, TQueryResponse> ThenResponse(TQueryResponse expectedResponse)
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
    public SingleProjectionQueryTest<TAggregatePayload, TSingleProjection, TSingleProjectionPayload, TQuery,
        TQueryParameter, TQueryResponse> ThenGetResponse(Action<TQueryResponse> responseAction)
    {
        Assert.NotNull(Response);
        responseAction(Response!);
        return this;
    }

    public SingleProjectionQueryTest<TAggregatePayload, TSingleProjection, TSingleProjectionPayload, TQuery,
        TQueryParameter, TQueryResponse> ThenResponseFromJson(string responseJson)
    {
        var response = JsonSerializer.Deserialize<TQueryResponse>(responseJson);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenResponse(response);
        return this;
    }
    public SingleProjectionQueryTest<TAggregatePayload, TSingleProjection, TSingleProjectionPayload, TQuery,
        TQueryParameter, TQueryResponse> ThenResponseFromFile(string responseFilename)
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<TQueryResponse>(openStream);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenResponse(response);
        return this;
    }
}
