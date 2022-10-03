using Sekiban.EventSourcing.Queries.SingleAggregates;
using Sekiban.EventSourcing.Shared;
using Xunit;
namespace Sekiban.EventSourcing.TestHelpers;

public class SingleProjectionTestEventSubscriber<TAggregate, TProjection, TProjectionContents> : ITestHelperEventSubscriber
    where TAggregate : AggregateBase, new()
    where TProjection : SingleAggregateProjectionBase<TAggregate, TProjection, TProjectionContents>, new()
    where TProjectionContents : ISingleAggregateProjectionContents
{
    public TProjection Projection { get; } = new();
    public SingleProjectionTestEventSubscriber()
    {
        OnEvent = ev =>
        {
            Projection.ApplyEvent(ev);
        };
    }

    public Action<IAggregateEvent> OnEvent
    {
        get;
    }

    public SingleAggregateProjectionDto<TProjectionContents> GetProjectionDto()
    {
        return Projection.ToDto();
    }
    public SingleProjectionTestEventSubscriber<TAggregate, TProjection, TProjectionContents> ThenDto(
        SingleAggregateProjectionDto<TProjectionContents> dto)
    {
        var actual = GetProjectionDto();
        var expected = dto with
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
    public SingleProjectionTestEventSubscriber<TAggregate, TProjection, TProjectionContents> ThenContents(TProjectionContents dtoContents)
    {
        var actual = GetProjectionDto().Contents;
        var expected = dtoContents;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public SingleProjectionTestEventSubscriber<TAggregate, TProjection, TProjectionContents> ThenContentsFromJson(string dtoContentsJson)
    {
        var actual = GetProjectionDto().Contents;
        var contents = JsonSerializer.Deserialize<TProjectionContents>(dtoContentsJson);
        if (contents is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        var expected = contents;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public SingleProjectionTestEventSubscriber<TAggregate, TProjection, TProjectionContents> ThenContentsFromFile(string dtoContentsFilename)
    {
        using var openStream = File.OpenRead(dtoContentsFilename);
        var actual = GetProjectionDto().Contents;
        var contents = JsonSerializer.Deserialize<TProjectionContents>(openStream);
        if (contents is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        var expected = contents;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public SingleProjectionTestEventSubscriber<TAggregate, TProjection, TProjectionContents> WriteProjectionDto(string filename)
    {
        var dto = GetProjectionDto();
        var json = SekibanJsonHelper.Serialize(dto);
        File.WriteAllText(filename, json);
        return this;
    }
}
