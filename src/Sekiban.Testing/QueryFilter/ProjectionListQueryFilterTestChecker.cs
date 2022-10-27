using Sekiban.Core.Query.MultipleAggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Shared;
using System.Text.Json;
using Xunit;
namespace Sekiban.Testing.QueryFilter;

public class
    ProjectionListQueryFilterTestChecker<TProjection, TProjectionPayload, TProjectionQueryFilter, TQueryFilterParameter, TQueryFilterResponse> :
        IQueryFilterChecker<MultipleAggregateProjectionState<TProjectionPayload>>
    where TProjection : MultipleAggregateProjectionBase<TProjectionPayload>, new()
    where TProjectionPayload : IMultipleAggregateProjectionPayload, new()
    where TQueryFilterParameter : IQueryParameter
    where TProjectionQueryFilter : IProjectionListQueryFilterDefinition<TProjection, TProjectionPayload, TQueryFilterParameter, TQueryFilterResponse>
    , new()
{
    private QueryFilterListResult<TQueryFilterResponse>? _response;



    private MultipleAggregateProjectionState<TProjectionPayload>? _state;
    public QueryFilterHandler? QueryFilterHandler { get; set; } = null;
    public void RegisterState(MultipleAggregateProjectionState<TProjectionPayload> state)
    {
        _state = state;
    }

    public ProjectionListQueryFilterTestChecker<TProjection, TProjectionPayload, TProjectionQueryFilter, TQueryFilterParameter, TQueryFilterResponse>
        WhenParam(TQueryFilterParameter param)
    {
        if (_state == null)
        {
            throw new InvalidDataException("Projection is null");
        }
        if (QueryFilterHandler == null) { throw new MissingMemberException(nameof(QueryFilterHandler)); }
        _response = QueryFilterHandler
            .GetProjectionListQueryFilter<TProjection, TProjectionPayload, TProjectionQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
                param,
                _state);
        return this;
    }
    public ProjectionListQueryFilterTestChecker<TProjection, TProjectionPayload, TProjectionQueryFilter, TQueryFilterParameter, TQueryFilterResponse>
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
    public ProjectionListQueryFilterTestChecker<TProjection, TProjectionPayload, TProjectionQueryFilter, TQueryFilterParameter, TQueryFilterResponse>
        ThenResponseIs(QueryFilterListResult<TQueryFilterResponse> expectedResponse)
    {
        var actual = _response;
        var expected = expectedResponse;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public ProjectionListQueryFilterTestChecker<TProjection, TProjectionPayload, TProjectionQueryFilter, TQueryFilterParameter, TQueryFilterResponse>
        ThenGetResponse(Action<QueryFilterListResult<TQueryFilterResponse>> responseAction)
    {
        Assert.NotNull(_response);
        responseAction(_response!);
        return this;
    }
    public ProjectionListQueryFilterTestChecker<TProjection, TProjectionPayload, TProjectionQueryFilter, TQueryFilterParameter, TQueryFilterResponse>
        ThenResponseIsFromJson(string responseJson)
    {
        var response = JsonSerializer.Deserialize<QueryFilterListResult<TQueryFilterResponse>>(responseJson);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenResponseIs(response);
        return this;
    }
    public ProjectionListQueryFilterTestChecker<TProjection, TProjectionPayload, TProjectionQueryFilter, TQueryFilterParameter, TQueryFilterResponse>
        ThenResponseIsFromFile(string responseFilename)
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<QueryFilterListResult<TQueryFilterResponse>>(openStream);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenResponseIs(response);
        return this;
    }
}
