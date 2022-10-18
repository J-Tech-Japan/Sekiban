using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.QueryModels;
using Sekiban.EventSourcing.Queries.QueryModels.Parameters;
using Sekiban.EventSourcing.Shared;
using Xunit;
namespace Sekiban.EventSourcing.TestHelpers.QueryFilters;

public class
    AggregateQueryFilterTestChecker<TAggregate, TAggregateContents, TQueryFilter, TQueryParameter, TResponseQueryModel> : IQueryFilterChecker<
        MultipleAggregateProjectionContentsDto<SingleAggregateListProjectionDto<AggregateDto<TAggregateContents>>>>
    where TAggregate : AggregateBase<TAggregateContents>
    where TAggregateContents : IAggregateContents, new()
    where TQueryFilter : IAggregateQueryFilterDefinition<TAggregate, TAggregateContents, TQueryParameter, TResponseQueryModel>
    where TQueryParameter : IQueryParameter
{
    public TResponseQueryModel? Response { get; set; }
    private MultipleAggregateProjectionContentsDto<SingleAggregateListProjectionDto<AggregateDto<TAggregateContents>>>? _dto { get; set; }
    public QueryFilterHandler? QueryFilterHandler
    {
        get;
        set;
    }
    public void RegisterDto(MultipleAggregateProjectionContentsDto<SingleAggregateListProjectionDto<AggregateDto<TAggregateContents>>> dto)
    {
        _dto = dto;
    }

    public AggregateQueryFilterTestChecker<TAggregate, TAggregateContents, TQueryFilter, TQueryParameter, TResponseQueryModel> WhenParam(
        TQueryParameter param)
    {
        if (_dto == null)
        {
            throw new InvalidDataException("Projection is null");
        }
        if (QueryFilterHandler == null) { throw new MissingMemberException(nameof(QueryFilterHandler)); }
        Response = QueryFilterHandler.GetAggregateQueryFilter<TAggregate, TAggregateContents, TQueryFilter, TQueryParameter, TResponseQueryModel>(
            param,
            _dto.Contents.List);
        return this;
    }
    public AggregateQueryFilterTestChecker<TAggregate, TAggregateContents, TQueryFilter, TQueryParameter, TResponseQueryModel> WriteResponse(
        string filename)
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
    public AggregateQueryFilterTestChecker<TAggregate, TAggregateContents, TQueryFilter, TQueryParameter, TResponseQueryModel> ThenResponse(
        TResponseQueryModel expectedResponse)
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
    public AggregateQueryFilterTestChecker<TAggregate, TAggregateContents, TQueryFilter, TQueryParameter, TResponseQueryModel> ThenResponseFromJson(
        string responseJson)
    {
        var response = JsonSerializer.Deserialize<TResponseQueryModel>(responseJson);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenResponse(response);
        return this;
    }
    public AggregateQueryFilterTestChecker<TAggregate, TAggregateContents, TQueryFilter, TQueryParameter, TResponseQueryModel> ThenResponseFromFile(
        string responseFilename)
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<TResponseQueryModel>(openStream);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenResponse(response);
        return this;
    }
}
