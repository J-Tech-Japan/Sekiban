using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.MultipleProjections;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Shared;
using System.Text.Json;
using Xunit;
namespace Sekiban.Testing.Queries;

public class
    SingleProjectionListQueryTestChecker<TAggregate, TSingleProjection, TAggregateProjectionPayload, TQuery,
        TQueryParameter, TResponseQueryModel> : IQueryChecker<MultiProjectionState<
        SingleProjectionListState<SingleProjectionState<TAggregateProjectionPayload>>>>
    where TAggregate : IAggregatePayload, new()
    where TSingleProjection : SingleProjectionBase<TAggregate, TSingleProjection, TAggregateProjectionPayload>, new
    ()
    where TAggregateProjectionPayload : ISingleProjectionPayload
    where TQuery : ISingleProjectionListQuery<TAggregate, TSingleProjection,
        TAggregateProjectionPayload, TQueryParameter, TResponseQueryModel>
    where TQueryParameter : IQueryParameter
{
    public QueryListResult<TResponseQueryModel>? Response { get; set; }
    private MultiProjectionState<SingleProjectionListState<SingleProjectionState<TAggregateProjectionPayload>>>
        ? _state { get; set; }
    public QueryHandler? QueryHandler
    {
        get;
        set;
    }
    public void RegisterState(
        MultiProjectionState<SingleProjectionListState<SingleProjectionState<TAggregateProjectionPayload>>>
            state)
    {
        _state = state;
    }

    public SingleProjectionListQueryTestChecker<TAggregate, TSingleProjection, TAggregateProjectionPayload,
        TQuery, TQueryParameter, TResponseQueryModel> WhenParam(TQueryParameter param)
    {
        if (_state == null)
        {
            throw new InvalidDataException("Projection is null");
        }
        if (QueryHandler == null) { throw new MissingMemberException(nameof(QueryHandler)); }
        Response = QueryHandler
            .GetSingleProjectionListQuery<TAggregate, TSingleProjection, TAggregateProjectionPayload, TQuery,
                TQueryParameter, TResponseQueryModel>(param, _state.Payload.List);
        return this;
    }
    public SingleProjectionListQueryTestChecker<TAggregate, TSingleProjection, TAggregateProjectionPayload,
        TQuery, TQueryParameter, TResponseQueryModel> WriteResponseToFile(string filename)
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
    public SingleProjectionListQueryTestChecker<TAggregate, TSingleProjection, TAggregateProjectionPayload,
        TQuery, TQueryParameter, TResponseQueryModel> ThenResponseIs(QueryListResult<TResponseQueryModel> expectedResponse)
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
    public SingleProjectionListQueryTestChecker<TAggregate, TSingleProjection, TAggregateProjectionPayload,
        TQuery, TQueryParameter, TResponseQueryModel> ThenGetResponse(Action<QueryListResult<TResponseQueryModel>> responseAction)
    {
        Assert.NotNull(Response);
        responseAction(Response!);
        return this;
    }

    public SingleProjectionListQueryTestChecker<TAggregate, TSingleProjection, TAggregateProjectionPayload,
        TQuery, TQueryParameter, TResponseQueryModel> ThenResponseIsFromJson(string responseJson)
    {
        var response = JsonSerializer.Deserialize<QueryListResult<TResponseQueryModel>>(responseJson);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenResponseIs(response);
        return this;
    }
    public SingleProjectionListQueryTestChecker<TAggregate, TSingleProjection, TAggregateProjectionPayload,
        TQuery, TQueryParameter, TResponseQueryModel> ThenResponseIsFromFile(string responseFilename)
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<QueryListResult<TResponseQueryModel>>(openStream);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenResponseIs(response);
        return this;
    }
}
