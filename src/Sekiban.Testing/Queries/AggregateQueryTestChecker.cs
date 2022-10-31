using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.MultipleProjections;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Shared;
using System.Text.Json;
using Xunit;
namespace Sekiban.Testing.Queries;

public class
    AggregateQueryTestChecker<TAggregatePayload, TQuery, TQueryParameter, TResponseQueryModel> : IQueryChecker<
        MultiProjectionState<SingleProjectionListState<AggregateState<TAggregatePayload>>>>
    where TAggregatePayload : IAggregatePayload, new()
    where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TResponseQueryModel>
    where TQueryParameter : IQueryParameter
{
    public TResponseQueryModel? Response { get; set; }
    private MultiProjectionState<SingleProjectionListState<AggregateState<TAggregatePayload>>>? _state { get; set; }
    public QueryHandler? QueryHandler
    {
        get;
        set;
    }
    public void RegisterState(MultiProjectionState<SingleProjectionListState<AggregateState<TAggregatePayload>>> state)
    {
        _state = state;
    }

    public AggregateQueryTestChecker<TAggregatePayload, TQuery, TQueryParameter, TResponseQueryModel> WhenParam(
        TQueryParameter param)
    {
        if (_state == null)
        {
            throw new InvalidDataException("Projection is null");
        }
        if (QueryHandler == null) { throw new MissingMemberException(nameof(QueryHandler)); }
        Response = QueryHandler.GetAggregateQuery<TAggregatePayload, TQuery, TQueryParameter, TResponseQueryModel>(
            param,
            _state.Payload.List);
        return this;
    }
    public AggregateQueryTestChecker<TAggregatePayload, TQuery, TQueryParameter, TResponseQueryModel> WriteResponseToFile(
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
    public AggregateQueryTestChecker<TAggregatePayload, TQuery, TQueryParameter, TResponseQueryModel> ThenResponseIs(
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
    public AggregateQueryTestChecker<TAggregatePayload, TQuery, TQueryParameter, TResponseQueryModel> ThenGetResponse(
        Action<TResponseQueryModel> responseAction)
    {
        Assert.NotNull(Response);
        responseAction(Response!);
        return this;
    }

    public AggregateQueryTestChecker<TAggregatePayload, TQuery, TQueryParameter, TResponseQueryModel> ThenResponseIsFromJson(
        string responseJson)
    {
        var response = JsonSerializer.Deserialize<TResponseQueryModel>(responseJson);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenResponseIs(response);
        return this;
    }
    public AggregateQueryTestChecker<TAggregatePayload, TQuery, TQueryParameter, TResponseQueryModel> ThenResponseIsFromFile(
        string responseFilename)
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<TResponseQueryModel>(openStream);
        if (response is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        ThenResponseIs(response);
        return this;
    }
}
