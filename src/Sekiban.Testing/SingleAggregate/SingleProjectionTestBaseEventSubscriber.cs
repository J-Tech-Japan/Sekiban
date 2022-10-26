using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.SingleAggregate;
using Sekiban.Core.Shared;
using System.Text.Json;
using Xunit;
namespace Sekiban.Testing.SingleAggregate;

public class SingleAggregateProjectionTestBase<TAggregate, TProjection, TAggregateProjectionPayload> : SingleAggregateTestBase
    where TAggregate : IAggregatePayload, new()
    where TProjection : SingleAggregateProjectionBase<TAggregate, TProjection, TAggregateProjectionPayload>, new()
    where TAggregateProjectionPayload : ISingleAggregateProjectionPayload
{
    public SingleAggregateProjectionTestBase(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }
    public TProjection Projection { get; } = new();

    public SingleAggregateProjectionState<TAggregateProjectionPayload> GetProjectionState()
    {
        var singleAggregateService = _serviceProvider.GetService<ISingleAggregateService>();
        if (singleAggregateService is null) { throw new Exception("ISingleAggregateService not found"); }
        var projectionResult = singleAggregateService.GetProjectionAsync<TAggregate, TProjection, TAggregateProjectionPayload>(AggregateId);
        var projectionState = projectionResult.Result;
        if (projectionState is null) { throw new Exception("Projection not found"); }
        return projectionState;
    }
    public SingleAggregateProjectionTestBase<TAggregate, TProjection, TAggregateProjectionPayload> ThenStateIs(
        SingleAggregateProjectionState<TAggregateProjectionPayload> state)
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
    public SingleAggregateProjectionTestBase<TAggregate, TProjection, TAggregateProjectionPayload> ThenPayloadIs(TAggregateProjectionPayload payload)
    {
        var actual = GetProjectionState().Payload;
        var expected = payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public SingleAggregateProjectionTestBase<TAggregate, TProjection, TAggregateProjectionPayload> ThenPayloadIsFromJson(string payloadJson)
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
    public SingleAggregateProjectionTestBase<TAggregate, TProjection, TAggregateProjectionPayload> ThenPayloadIsFromFile(string payloadFilename)
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
    public SingleAggregateProjectionTestBase<TAggregate, TProjection, TAggregateProjectionPayload> WriteProjectionStateToFile(string filename)
    {
        var state = GetProjectionState();
        var json = SekibanJsonHelper.Serialize(state);
        File.WriteAllText(filename, json);
        return this;
    }
}
