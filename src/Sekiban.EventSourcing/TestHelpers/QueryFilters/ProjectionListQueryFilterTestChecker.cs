using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.QueryModels;
using Sekiban.EventSourcing.Queries.QueryModels.Parameters;
using Sekiban.EventSourcing.Shared;
using Xunit;
namespace Sekiban.EventSourcing.TestHelpers.QueryFilters;

public class
    ProjectionListQueryFilterTestChecker<TProjection, TProjectionContents, TProjectionQueryFilter, TQueryFilterParameter, TQueryFilterResponse> :
        IQueryFilterChecker<MultipleAggregateProjectionContentsDto<TProjectionContents>>
    where TProjection : MultipleAggregateProjectionBase<TProjectionContents>, new()
    where TProjectionContents : IMultipleAggregateProjectionContents, new()
    where TQueryFilterParameter : IQueryParameter
    where TProjectionQueryFilter : IProjectionListQueryFilterDefinition<TProjection, TProjectionContents, TQueryFilterParameter, TQueryFilterResponse>
    , new()
{



    private MultipleAggregateProjectionContentsDto<TProjectionContents>? _dto;
    private QueryFilterListResult<TQueryFilterResponse>? _response;
    public QueryFilterHandler? QueryFilterHandler { get; set; } = null;
    public void RegisterDto(MultipleAggregateProjectionContentsDto<TProjectionContents> dto)
    {
        _dto = dto;
    }

    public ProjectionListQueryFilterTestChecker<TProjection, TProjectionContents, TProjectionQueryFilter, TQueryFilterParameter, TQueryFilterResponse>
        WhenParam(TQueryFilterParameter param)
    {
        if (_dto == null)
        {
            throw new InvalidDataException("Projection is null");
        }
        if (QueryFilterHandler == null) { throw new MissingMemberException(nameof(QueryFilterHandler)); }
        _response = QueryFilterHandler
            .GetProjectionListQueryFilter<TProjection, TProjectionContents, TProjectionQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
                param,
                _dto);
        return this;
    }
    public ProjectionListQueryFilterTestChecker<TProjection, TProjectionContents, TProjectionQueryFilter, TQueryFilterParameter, TQueryFilterResponse>
        WriteResponse(string filename)
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
    public ProjectionListQueryFilterTestChecker<TProjection, TProjectionContents, TProjectionQueryFilter, TQueryFilterParameter, TQueryFilterResponse>
        ThenResponse(QueryFilterListResult<TQueryFilterResponse> expectedResponse)
    {
        var actual = _response;
        var expected = expectedResponse;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public ProjectionListQueryFilterTestChecker<TProjection, TProjectionContents, TProjectionQueryFilter, TQueryFilterParameter, TQueryFilterResponse>
        ThenResponseFromJson(string responseJson)
    {
        var response = JsonSerializer.Deserialize<QueryFilterListResult<TQueryFilterResponse>>(responseJson);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenResponse(response);
        return this;
    }
    public ProjectionListQueryFilterTestChecker<TProjection, TProjectionContents, TProjectionQueryFilter, TQueryFilterParameter, TQueryFilterResponse>
        ThenResponseFromFile(string responseFilename)
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<QueryFilterListResult<TQueryFilterResponse>>(openStream);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenResponse(response);
        return this;
    }
}
