using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.MultipleAggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleAggregate;
using Sekiban.Core.Shared;
using System.Text.Json;
using Xunit;
namespace Sekiban.Testing.QueryFilter;

public class SingleAggregateListProjectionQueryFilterTestChecker<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload, TQueryFilter,
    TQueryParameter, TResponseQueryModel> : IQueryFilterChecker<MultipleAggregateProjectionState<
    SingleAggregateListProjectionState<SingleAggregateProjectionState<TAggregateProjectionPayload>>>>
    where TAggregate : IAggregatePayload, new()
    where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload>, new
    ()
    where TAggregateProjectionPayload : ISingleAggregateProjectionPayload
    where TQueryFilter : ISingleAggregateProjectionQueryFilterDefinition<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload,
        TQueryParameter, TResponseQueryModel>
    where TQueryParameter : IQueryParameter
{
    public TResponseQueryModel? Response { get; set; }
    private MultipleAggregateProjectionState<SingleAggregateListProjectionState<SingleAggregateProjectionState<TAggregateProjectionPayload>>>
        ? _state { get; set; }
    public QueryFilterHandler? QueryFilterHandler
    {
        get;
        set;
    }
    public void RegisterState(
        MultipleAggregateProjectionState<SingleAggregateListProjectionState<SingleAggregateProjectionState<TAggregateProjectionPayload>>>
            state)
    {
        _state = state;
    }

    public SingleAggregateListProjectionQueryFilterTestChecker<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload
        , TQueryFilter, TQueryParameter, TResponseQueryModel> WhenParam(TQueryParameter param)
    {
        if (_state == null)
        {
            throw new InvalidDataException("Projection is null");
        }
        if (QueryFilterHandler == null) { throw new MissingMemberException(nameof(QueryFilterHandler)); }
        Response = QueryFilterHandler
            .GetSingleAggregateProjectionQueryFilter<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload, TQueryFilter,
                TQueryParameter, TResponseQueryModel>(param, _state.Payload.List);
        return this;
    }
    public SingleAggregateListProjectionQueryFilterTestChecker<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload,
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
    public SingleAggregateListProjectionQueryFilterTestChecker<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload, TQueryFilter,
        TQueryParameter, TResponseQueryModel> ThenResponse(TResponseQueryModel expectedResponse)
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
    public SingleAggregateListProjectionQueryFilterTestChecker<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload, TQueryFilter,
        TQueryParameter, TResponseQueryModel> ThenGetResponse(Action<TResponseQueryModel> responseAction)
    {
        Assert.NotNull(Response);
        responseAction(Response!);
        return this;
    }

    public SingleAggregateListProjectionQueryFilterTestChecker<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload, TQueryFilter,
        TQueryParameter, TResponseQueryModel> ThenResponseFromJson(string responseJson)
    {
        var response = JsonSerializer.Deserialize<TResponseQueryModel>(responseJson);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenResponse(response);
        return this;
    }
    public SingleAggregateListProjectionQueryFilterTestChecker<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload, TQueryFilter,
        TQueryParameter, TResponseQueryModel> ThenResponseFromFile(string responseFilename)
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<TResponseQueryModel>(openStream);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenResponse(response);
        return this;
    }
}
