using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.MultipleAggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleAggregate;
using Sekiban.Core.Shared;
using System;
using System.IO;
using System.Text.Json;
using Xunit;
namespace Sekiban.Testing.QueryFilter;

public class
    SingleAggregateListProjectionListQueryFilterTestChecker<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents, TQueryFilter,
        TQueryParameter, TResponseQueryModel> : IQueryFilterChecker<MultipleAggregateProjectionContentsDto<
        SingleAggregateListProjectionDto<SingleAggregateProjectionDto<TSingleAggregateProjectionContents>>>> where TAggregate : AggregateCommonBase, new()
    where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>, new
    ()
    where TSingleAggregateProjectionContents : ISingleAggregateProjectionContents
    where TQueryFilter : ISingleAggregateProjectionListQueryFilterDefinition<TAggregate, TSingleAggregateProjection,
        TSingleAggregateProjectionContents, TQueryParameter, TResponseQueryModel>
    where TQueryParameter : IQueryParameter
{
    public QueryFilterListResult<TResponseQueryModel>? Response { get; set; }
    private MultipleAggregateProjectionContentsDto<SingleAggregateListProjectionDto<SingleAggregateProjectionDto<TSingleAggregateProjectionContents>>>
        ? _dto { get; set; }
    public QueryFilterHandler? QueryFilterHandler
    {
        get;
        set;
    }
    public void RegisterDto(
        MultipleAggregateProjectionContentsDto<SingleAggregateListProjectionDto<SingleAggregateProjectionDto<TSingleAggregateProjectionContents>>>
            dto)
    {
        _dto = dto;
    }

    public SingleAggregateListProjectionListQueryFilterTestChecker<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents,
        TQueryFilter, TQueryParameter, TResponseQueryModel> WhenParam(TQueryParameter param)
    {
        if (_dto == null)
        {
            throw new InvalidDataException("Projection is null");
        }
        if (QueryFilterHandler == null) { throw new MissingMemberException(nameof(QueryFilterHandler)); }
        Response = QueryFilterHandler
            .GetSingleAggregateProjectionListQueryFilter<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents, TQueryFilter,
                TQueryParameter, TResponseQueryModel>(param, _dto.Contents.List);
        return this;
    }
    public SingleAggregateListProjectionListQueryFilterTestChecker<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents,
        TQueryFilter, TQueryParameter, TResponseQueryModel> WriteResponse(string filename)
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
    public SingleAggregateListProjectionListQueryFilterTestChecker<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents,
        TQueryFilter, TQueryParameter, TResponseQueryModel> ThenResponse(QueryFilterListResult<TResponseQueryModel> expectedResponse)
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
    public SingleAggregateListProjectionListQueryFilterTestChecker<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents,
        TQueryFilter, TQueryParameter, TResponseQueryModel> ThenResponseFromJson(string responseJson)
    {
        var response = JsonSerializer.Deserialize<QueryFilterListResult<TResponseQueryModel>>(responseJson);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenResponse(response);
        return this;
    }
    public SingleAggregateListProjectionListQueryFilterTestChecker<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents,
        TQueryFilter, TQueryParameter, TResponseQueryModel> ThenResponseFromFile(string responseFilename)
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<QueryFilterListResult<TResponseQueryModel>>(openStream);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenResponse(response);
        return this;
    }
}