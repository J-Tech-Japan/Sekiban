using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.SingleAggregate;
using Sekiban.Core.Shared;
using System.Text.Json;
using Xunit;
namespace Sekiban.Testing.SingleAggregate;

public class SingleAggregateProjectionTestBase<TAggregate, TProjection, TProjectionContents> : SingleAggregateTestBase
    where TAggregate : AggregateCommonBase, new()
    where TProjection : SingleAggregateProjectionBase<TAggregate, TProjection, TProjectionContents>, new()
    where TProjectionContents : ISingleAggregateProjectionPayload
{
    public TProjection Projection { get; } = new();
    public SingleAggregateProjectionTestBase(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    public SingleAggregateProjectionDto<TProjectionContents> GetProjectionDto()
    {
        var singleAggregateService = _serviceProvider.GetService<ISingleAggregateService>();
        if (singleAggregateService is null) { throw new Exception("ISingleAggregateService not found"); }
        var projectionResult = singleAggregateService.GetProjectionAsync<TAggregate, TProjection, TProjectionContents>(AggregateId);
        var projectionDto = projectionResult.Result;
        if (projectionDto is null) { throw new Exception("Projection not found"); }
        return projectionDto;
    }
    public SingleAggregateProjectionTestBase<TAggregate, TProjection, TProjectionContents> ThenDtoIs(
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
    public SingleAggregateProjectionTestBase<TAggregate, TProjection, TProjectionContents> ThenContentsIs(TProjectionContents dtoContents)
    {
        var actual = GetProjectionDto().Payload;
        var expected = dtoContents;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public SingleAggregateProjectionTestBase<TAggregate, TProjection, TProjectionContents> ThenContentsIsFromJson(string dtoContentsJson)
    {
        var actual = GetProjectionDto().Payload;
        var contents = JsonSerializer.Deserialize<TProjectionContents>(dtoContentsJson);
        if (contents is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        var expected = contents;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public SingleAggregateProjectionTestBase<TAggregate, TProjection, TProjectionContents> ThenContentsIsFromFile(string dtoContentsFilename)
    {
        using var openStream = File.OpenRead(dtoContentsFilename);
        var actual = GetProjectionDto().Payload;
        var contents = JsonSerializer.Deserialize<TProjectionContents>(openStream);
        if (contents is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        var expected = contents;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public SingleAggregateProjectionTestBase<TAggregate, TProjection, TProjectionContents> WriteProjectionDtoToFile(string filename)
    {
        var dto = GetProjectionDto();
        var json = SekibanJsonHelper.Serialize(dto);
        File.WriteAllText(filename, json);
        return this;
    }
}
