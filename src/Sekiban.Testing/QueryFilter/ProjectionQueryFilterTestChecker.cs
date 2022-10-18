using Sekiban.Core.Query.MultipleAggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Shared;
using System;
using System.IO;
using System.Text.Json;
using Xunit;
namespace Sekiban.Testing.QueryFilter;

public class
    ProjectionQueryFilterTestChecker<TProjection, TProjectionContents, TProjectionQueryFilter, TQueryFilterParameter, TQueryFilterResponse> :
        IQueryFilterChecker<MultipleAggregateProjectionContentsDto<TProjectionContents>>
    where TProjection : MultipleAggregateProjectionBase<TProjectionContents>, new()
    where TProjectionContents : IMultipleAggregateProjectionContents, new()
    where TQueryFilterParameter : IQueryParameter
    where TProjectionQueryFilter : IProjectionQueryFilterDefinition<TProjection, TProjectionContents, TQueryFilterParameter, TQueryFilterResponse>,
    new()
{
    private MultipleAggregateProjectionContentsDto<TProjectionContents>? _dto;
    private TQueryFilterResponse? _response;
    public QueryFilterHandler? QueryFilterHandler { get; set; } = null;
    public void RegisterDto(MultipleAggregateProjectionContentsDto<TProjectionContents> dto)
    {
        _dto = dto;
    }

    public ProjectionQueryFilterTestChecker<TProjection, TProjectionContents, TProjectionQueryFilter, TQueryFilterParameter, TQueryFilterResponse>
        WhenParam(TQueryFilterParameter param)
    {
        if (_dto == null)
        {
            throw new InvalidDataException("Projection is null");
        }
        if (QueryFilterHandler == null) { throw new MissingMemberException(nameof(QueryFilterHandler)); }
        _response = QueryFilterHandler
            .GetProjectionQueryFilter<TProjection, TProjectionContents, TProjectionQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
                param,
                _dto);
        return this;
    }
    public ProjectionQueryFilterTestChecker<TProjection, TProjectionContents, TProjectionQueryFilter, TQueryFilterParameter, TQueryFilterResponse>
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
    public ProjectionQueryFilterTestChecker<TProjection, TProjectionContents, TProjectionQueryFilter, TQueryFilterParameter, TQueryFilterResponse>
        ThenResponse(TQueryFilterResponse expectedResponse)
    {
        var actual = _response;
        var expected = expectedResponse;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public ProjectionQueryFilterTestChecker<TProjection, TProjectionContents, TProjectionQueryFilter, TQueryFilterParameter, TQueryFilterResponse>
        ThenResponseFromJson(string responseJson)
    {
        var response = JsonSerializer.Deserialize<TQueryFilterResponse>(responseJson);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenResponse(response);
        return this;
    }
    public ProjectionQueryFilterTestChecker<TProjection, TProjectionContents, TProjectionQueryFilter, TQueryFilterParameter, TQueryFilterResponse>
        ThenResponseFromFile(string responseFilename)
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<TQueryFilterResponse>(openStream);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenResponse(response);
        return this;
    }
}
