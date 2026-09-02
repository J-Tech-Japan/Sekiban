using System.Text.Json;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Tests.SerializedCommitWire;

/// <summary>
///     Pins response member spellings and CLR types without broadening the request-only SerializedCommitWire JSON context.
/// </summary>
public sealed class SerializedCommitInteropResponseShapeTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public void SerializableTagState_ResponseShape_PinsProjectorVersionAndLastSortedUniqueId()
    {
        AssertPropertyType<SerializableTagState>(nameof(SerializableTagState.ProjectorVersion), typeof(string));
        AssertPropertyType<SerializableTagState>(nameof(SerializableTagState.LastSortedUniqueId), typeof(string));

        var state = new SerializableTagState(
            "{}"u8.ToArray(),
            1,
            "063082281600000000000000000000",
            "Order",
            "42",
            "OrderProjector",
            "OrderState",
            "1");
        using var json = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(state, WebJson));
        Assert.Equal(JsonValueKind.String, json.RootElement.GetProperty("projectorVersion").ValueKind);
        Assert.Equal("1", json.RootElement.GetProperty("projectorVersion").GetString());
        Assert.Equal("063082281600000000000000000000", json.RootElement.GetProperty("lastSortedUniqueId").GetString());
    }

    [Fact]
    public void SerializedCommitResult_ResponseShape_PinsWrittenEventsTagWriteResultsAndDuration()
    {
        AssertPropertyType<SerializedCommitResult>(nameof(SerializedCommitResult.WrittenEvents), typeof(IReadOnlyList<SerializableEvent>));
        AssertPropertyType<SerializedCommitResult>(nameof(SerializedCommitResult.TagWriteResults), typeof(IReadOnlyList<TagWriteResult>));
        AssertPropertyType<SerializedCommitResult>(nameof(SerializedCommitResult.Duration), typeof(TimeSpan));

        var result = new SerializedCommitResult(
            [CreateEvent()],
            [new TagWriteResult("Order:42", 1, DateTimeOffset.UnixEpoch)],
            TimeSpan.FromSeconds(1));
        using var json = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(result, WebJson));
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("writtenEvents").ValueKind);
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("tagWriteResults").ValueKind);
        Assert.True(json.RootElement.TryGetProperty("duration", out _));
    }

    [Fact]
    public void SerializableEvent_ResponseShape_PinsEventMetadataMembersAndClrTypes()
    {
        AssertPropertyType<SerializableEvent>(nameof(SerializableEvent.Payload), typeof(byte[]));
        AssertPropertyType<SerializableEvent>(nameof(SerializableEvent.SortableUniqueIdValue), typeof(string));
        AssertPropertyType<SerializableEvent>(nameof(SerializableEvent.Id), typeof(Guid));
        AssertPropertyType<SerializableEvent>(nameof(SerializableEvent.EventMetadata), typeof(EventMetadata));
        AssertPropertyType<SerializableEvent>(nameof(SerializableEvent.Tags), typeof(List<string>));
        AssertPropertyType<SerializableEvent>(nameof(SerializableEvent.EventPayloadName), typeof(string));
        AssertPropertyType<EventMetadata>(nameof(EventMetadata.CausationId), typeof(string));
        AssertPropertyType<EventMetadata>(nameof(EventMetadata.CorrelationId), typeof(string));
        AssertPropertyType<EventMetadata>(nameof(EventMetadata.ExecutedUser), typeof(string));

        using var json = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(CreateEvent(), WebJson));
        var metadata = json.RootElement.GetProperty("eventMetadata");
        Assert.Equal("cause", metadata.GetProperty("causationId").GetString());
        Assert.Equal("correlation", metadata.GetProperty("correlationId").GetString());
        Assert.Equal("user", metadata.GetProperty("executedUser").GetString());
    }

    [Fact]
    public void TagWriteResult_ResponseShape_PinsTagVersionAndWrittenAtClrTypes()
    {
        AssertPropertyType<TagWriteResult>(nameof(TagWriteResult.Tag), typeof(string));
        AssertPropertyType<TagWriteResult>(nameof(TagWriteResult.Version), typeof(long));
        AssertPropertyType<TagWriteResult>(nameof(TagWriteResult.WrittenAt), typeof(DateTimeOffset));

        using var json = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(
            new TagWriteResult("Order:42", 1, DateTimeOffset.UnixEpoch), WebJson));
        Assert.Equal("Order:42", json.RootElement.GetProperty("tag").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("version").GetInt64());
        Assert.True(json.RootElement.TryGetProperty("writtenAt", out _));
    }

    [Fact]
    public void SerializedCommitWireContext_RemainsRequestOnly_NoResponseSourceGeneratedPin()
    {
        Assert.Null(SerializedCommitWireJsonContext.Default.GetTypeInfo(typeof(SerializableTagState)));
        Assert.Null(SerializedCommitWireJsonContext.Default.GetTypeInfo(typeof(SerializedCommitResult)));
        Assert.Null(SerializedCommitWireJsonContext.Default.GetTypeInfo(typeof(SerializableEvent)));
        Assert.Null(SerializedCommitWireJsonContext.Default.GetTypeInfo(typeof(TagWriteResult)));
    }

    private static SerializableEvent CreateEvent() => new(
        "{}"u8.ToArray(),
        "063082281600000000000000000000",
        Guid.Parse("99999999-9999-9999-9999-999999999999"),
        new EventMetadata("cause", "correlation", "user"),
        ["Order:42"],
        "ResponseEvent");

    private static void AssertPropertyType<T>(string propertyName, Type expectedType)
    {
        var property = typeof(T).GetProperty(propertyName);
        Assert.NotNull(property);
        Assert.Equal(expectedType, property!.PropertyType);
    }
}
