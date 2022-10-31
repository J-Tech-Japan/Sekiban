using Sekiban.Core.Query.MultipleProjections;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Shared;
using System.Text.Json;
using Xunit;
namespace Sekiban.Testing.Queries;

public class
    ProjectionQueryTestChecker<TProjection, TProjectionPayload, TProjectionQuery, TQueryParameter, TQueryResponse> :
        IQueryChecker<MultiProjectionState<TProjectionPayload>>
    where TProjection : MultiProjectionBase<TProjectionPayload>, new()
    where TProjectionPayload : IMultiProjectionPayload, new()
    where TQueryParameter : IQueryParameter
    where TProjectionQuery : IMultiProjectionQuery<TProjection, TProjectionPayload, TQueryParameter, TQueryResponse>,
    new()
{
    private TQueryResponse? _response;
    private MultiProjectionState<TProjectionPayload>? _state;
    public QueryHandler? QueryHandler { get; set; } = null;
    public void RegisterState(MultiProjectionState<TProjectionPayload> state)
    {
        _state = state;
    }

    public ProjectionQueryTestChecker<TProjection, TProjectionPayload, TProjectionQuery, TQueryParameter, TQueryResponse>
        WhenParam(TQueryParameter param)
    {
        if (_state == null)
        {
            throw new InvalidDataException("Projection is null");
        }
        if (QueryHandler == null) { throw new MissingMemberException(nameof(QueryHandler)); }
        _response = QueryHandler
            .GetMultiProjectionQuery<TProjection, TProjectionPayload, TProjectionQuery, TQueryParameter, TQueryResponse>(
                param,
                _state);
        return this;
    }
    public ProjectionQueryTestChecker<TProjection, TProjectionPayload, TProjectionQuery, TQueryParameter, TQueryResponse>
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
    public ProjectionQueryTestChecker<TProjection, TProjectionPayload, TProjectionQuery, TQueryParameter, TQueryResponse>
        ThenResponseIs(TQueryResponse expectedResponse)
    {
        var actual = _response;
        var expected = expectedResponse;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public ProjectionQueryTestChecker<TProjection, TProjectionPayload, TProjectionQuery, TQueryParameter, TQueryResponse>
        ThenGetResponse(Action<TQueryResponse> responseAction)
    {
        Assert.NotNull(_response);
        responseAction(_response!);
        return this;
    }

    public ProjectionQueryTestChecker<TProjection, TProjectionPayload, TProjectionQuery, TQueryParameter, TQueryResponse>
        ThenResponseIsFromJson(string responseJson)
    {
        var response = JsonSerializer.Deserialize<TQueryResponse>(responseJson);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenResponseIs(response);
        return this;
    }
    public ProjectionQueryTestChecker<TProjection, TProjectionPayload, TProjectionQuery, TQueryParameter, TQueryResponse>
        ThenResponseIsFromFile(string responseFilename)
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<TQueryResponse>(openStream);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenResponseIs(response);
        return this;
    }
}
