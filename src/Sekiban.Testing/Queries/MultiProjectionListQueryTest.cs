using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Shared;
using System.Text.Json;
using Xunit;
namespace Sekiban.Testing.Queries;

public class
    MultiProjectionListQueryTest<TProjection, TProjectionPayload, TQuery, TQueryParameter, TQueryResponse> :
        IQueryTest
    where TProjection : MultiProjectionBase<TProjectionPayload>, new()
    where TProjectionPayload : IMultiProjectionPayload, new()
    where TQueryParameter : IQueryParameter
    where TQuery : IMultiProjectionListQuery<TProjection, TProjectionPayload, TQueryParameter, TQueryResponse>
{
    private ListQueryResult<TQueryResponse>? _response;
    public IQueryService? QueryService { get; set; } = null;
    public MultiProjectionListQueryTest<TProjection, TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>
        WhenParam(TQueryParameter param)
    {
        if (QueryService == null) { throw new MissingMemberException(nameof(QueryService)); }
        _response = QueryService
            .GetMultiProjectionListQueryAsync<TProjection, TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(param)
            .Result;
        return this;
    }
    public MultiProjectionListQueryTest<TProjection, TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>
        WriteResponseToFile(string filename)
    {
        if (_response == null)
        {
            throw new InvalidDataException("Response is null");
        }
        var json = SekibanJsonHelper.Serialize(_response);
        if (string.IsNullOrEmpty(json))
        {
            throw new InvalidDataException("Json is null or empty");
        }
        File.WriteAllTextAsync(filename, json);
        return this;
    }
    public MultiProjectionListQueryTest<TProjection, TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>
        ThenResponseIs(ListQueryResult<TQueryResponse> expectedResponse)
    {
        var actual = _response;
        var expected = expectedResponse;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public MultiProjectionListQueryTest<TProjection, TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>
        ThenGetResponse(Action<ListQueryResult<TQueryResponse>> responseAction)
    {
        Assert.NotNull(_response);
        responseAction(_response!);
        return this;
    }
    public MultiProjectionListQueryTest<TProjection, TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>
        ThenResponseIsFromJson(string responseJson)
    {
        var response = JsonSerializer.Deserialize<ListQueryResult<TQueryResponse>>(responseJson);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenResponseIs(response);
        return this;
    }
    public MultiProjectionListQueryTest<TProjection, TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>
        ThenResponseIsFromFile(string responseFilename)
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<ListQueryResult<TQueryResponse>>(openStream);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenResponseIs(response);
        return this;
    }
}
