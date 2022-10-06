using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.QueryModels;
using Sekiban.EventSourcing.Queries.QueryModels.Parameters;
using Sekiban.EventSourcing.Queries.SingleAggregates;
using Sekiban.EventSourcing.Shared;
using Xunit;
namespace Sekiban.EventSourcing.TestHelpers;

public class
    SingleAggregateListProjectionListQueryFilterTestChecker<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents,
        TAggregateContents, TQueryFilter, TQueryParameter, TResponseQueryModel> : IQueryFilterChecker<MultipleAggregateProjectionContentsDto<
        SingleAggregateListProjectionDto<SingleAggregateProjectionDto<TSingleAggregateProjectionContents>>>> where TAggregate : AggregateBase, new()
    where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>, new
    ()
    where TSingleAggregateProjectionContents : ISingleAggregateProjectionContents
    where TQueryFilter : ISingleAggregateProjectionListQueryFilterDefinition<TAggregate, TSingleAggregateProjection,
        TSingleAggregateProjectionContents, TQueryParameter, TResponseQueryModel>
    where TQueryParameter : IQueryParameter
{
    public IEnumerable<TResponseQueryModel>? Response { get; set; }
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
        TAggregateContents, TQueryFilter, TQueryParameter, TResponseQueryModel> WhenParam(TQueryParameter param)
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
        TAggregateContents, TQueryFilter, TQueryParameter, TResponseQueryModel> WriteResponse(string filename)
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
        TAggregateContents, TQueryFilter, TQueryParameter, TResponseQueryModel> ThenResponse(IEnumerable<TResponseQueryModel> expectedResponse)
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
        TAggregateContents, TQueryFilter, TQueryParameter, TResponseQueryModel> ThenResponseFromJson(string responseJson)
    {
        var response = JsonSerializer.Deserialize<IEnumerable<TResponseQueryModel>>(responseJson);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenResponse(response);
        return this;
    }
    public SingleAggregateListProjectionListQueryFilterTestChecker<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents,
        TAggregateContents, TQueryFilter, TQueryParameter, TResponseQueryModel> ThenResponseFromFile(string responseFilename)
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<IEnumerable<TResponseQueryModel>>(openStream);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenResponse(response);
        return this;
    }
}
