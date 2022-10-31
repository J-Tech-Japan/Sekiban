using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Shared;
using System.Text.Json;
using Xunit;
namespace Sekiban.Testing.SingleAggregate;

public class SingleProjectionTest<TAggregate, TProjection, TAggregateProjectionPayload> : SingleAggregateTestBase
    where TAggregate : IAggregatePayload, new()
    where TProjection : SingleProjectionBase<TAggregate, TProjection, TAggregateProjectionPayload>, new()
    where TAggregateProjectionPayload : ISingleProjectionPayload
{
    public SingleProjectionTest(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }
    public TProjection Projection { get; } = new();

    public SingleProjectionState<TAggregateProjectionPayload> GetProjectionState()
    {
        var singleAggregateService = _serviceProvider.GetService<ISingleProjectionService>();
        if (singleAggregateService is null) { throw new Exception("ISingleProjectionService not found"); }
        var projectionResult = singleAggregateService.GetProjectionAsync<TAggregate, TProjection, TAggregateProjectionPayload>(AggregateId);
        var projectionState = projectionResult.Result;
        if (projectionState is null) { throw new Exception("Projection not found"); }
        return projectionState;
    }
    public SingleProjectionTest<TAggregate, TProjection, TAggregateProjectionPayload> ThenStateIs(
        SingleProjectionState<TAggregateProjectionPayload> state)
    {
        var actual = GetProjectionState();
        var expected = state with
        {
            LastEventId = actual.LastEventId,
            Version = actual.Version,
            AppliedSnapshotVersion = actual.AppliedSnapshotVersion,
            LastSortableUniqueId = actual.LastSortableUniqueId
        };
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public SingleProjectionTest<TAggregate, TProjection, TAggregateProjectionPayload> ThenPayloadIs(TAggregateProjectionPayload payload)
    {
        var actual = GetProjectionState().Payload;
        var expected = payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public SingleProjectionTest<TAggregate, TProjection, TAggregateProjectionPayload> ThenGetPayload(
        Action<TAggregateProjectionPayload> payloadAction)
    {
        payloadAction(GetProjectionState().Payload);
        return this;
    }
    public SingleProjectionTest<TAggregate, TProjection, TAggregateProjectionPayload> ThenGetState(
        Action<SingleProjectionState<TAggregateProjectionPayload>> stateAction)
    {
        stateAction(GetProjectionState());
        return this;
    }
    public SingleProjectionTest<TAggregate, TProjection, TAggregateProjectionPayload> ThenPayloadIsFromJson(string payloadJson)
    {
        var actual = GetProjectionState().Payload;
        var payload = JsonSerializer.Deserialize<TAggregateProjectionPayload>(payloadJson);
        if (payload is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        var expected = payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public SingleProjectionTest<TAggregate, TProjection, TAggregateProjectionPayload> ThenPayloadIsFromFile(string payloadFilename)
    {
        using var openStream = File.OpenRead(payloadFilename);
        var actual = GetProjectionState().Payload;
        var payload = JsonSerializer.Deserialize<TAggregateProjectionPayload>(openStream);
        if (payload is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        var expected = payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public SingleProjectionTest<TAggregate, TProjection, TAggregateProjectionPayload> WriteProjectionStateToFile(string filename)
    {
        var state = GetProjectionState();
        var json = SekibanJsonHelper.Serialize(state);
        File.WriteAllText(filename, json);
        return this;
    }
}
