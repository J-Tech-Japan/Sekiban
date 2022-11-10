using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Shared;
using System.Text.Json;
using Xunit;
namespace Sekiban.Testing.SingleProjections;

public class SingleProjectionTest<TSingleProjectionPayload> : SingleProjectionTestCommon
    where TSingleProjectionPayload : ISingleProjectionPayload, new()
{
    public SingleProjectionTest(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }
    public SingleProjection<TSingleProjectionPayload> Projection { get; } = new();

    public SingleProjectionState<TSingleProjectionPayload> GetProjectionState()
    {
        var singleProjectionService = _serviceProvider.GetService<ISingleProjectionService>();
        if (singleProjectionService is null) { throw new Exception("ISingleProjectionService not found"); }
        var projectionResult = singleProjectionService.GetProjectionAsync<TSingleProjectionPayload>(AggregateId);
        var projectionState = projectionResult.Result;
        if (projectionState is null) { throw new Exception("Projection not found"); }
        return projectionState;
    }
    public SingleProjectionTest<TSingleProjectionPayload> ThenStateIs(
        SingleProjectionState<TSingleProjectionPayload> state)
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
    public SingleProjectionTest<TSingleProjectionPayload> ThenPayloadIs(TSingleProjectionPayload payload)
    {
        var actual = GetProjectionState().Payload;
        var expected = payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public SingleProjectionTest<TSingleProjectionPayload> ThenGetPayload(
        Action<TSingleProjectionPayload> payloadAction)
    {
        payloadAction(GetProjectionState().Payload);
        return this;
    }
    public SingleProjectionTest<TSingleProjectionPayload> ThenGetState(
        Action<SingleProjectionState<TSingleProjectionPayload>> stateAction)
    {
        stateAction(GetProjectionState());
        return this;
    }
    public SingleProjectionTest<TSingleProjectionPayload> ThenPayloadIsFromJson(string payloadJson)
    {
        var actual = GetProjectionState().Payload;
        var payload = JsonSerializer.Deserialize<TSingleProjectionPayload>(payloadJson);
        if (payload is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        var expected = payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public SingleProjectionTest<TSingleProjectionPayload> ThenPayloadIsFromFile(string payloadFilename)
    {
        using var openStream = File.OpenRead(payloadFilename);
        var actual = GetProjectionState().Payload;
        var payload = JsonSerializer.Deserialize<TSingleProjectionPayload>(openStream);
        if (payload is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        var expected = payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public SingleProjectionTest<TSingleProjectionPayload> WriteProjectionStateToFile(string filename)
    {
        var state = GetProjectionState();
        var json = SekibanJsonHelper.Serialize(state);
        File.WriteAllText(filename, json);
        return this;
    }
}
