using System.Text;
using System.Text.Json;
using Sekiban.Dcb.ColdEvents;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.ColdEvents.Tests;

public class JsonlSegmentWriterTests
{
    private static SerializableEvent CreateEvent(string payloadName)
    {
        var sortableId = SortableUniqueId.Generate(
            new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc), Guid.NewGuid());
        return new SerializableEvent(
            Payload: Encoding.UTF8.GetBytes("{\"key\":\"value\"}"),
            SortableUniqueIdValue: sortableId,
            Id: Guid.NewGuid(),
            EventMetadata: new EventMetadata("cause", "corr", "user"),
            Tags: ["tag1"],
            EventPayloadName: payloadName);
    }

    [Fact]
    public void Should_write_one_json_line_per_event()
    {
        // Given
        var events = new[] { CreateEvent("Event1"), CreateEvent("Event2"), CreateEvent("Event3") };

        // When
        var bytes = JsonlSegmentWriter.Write(events);
        var text = Encoding.UTF8.GetString(bytes);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Then
        Assert.Equal(3, lines.Length);
    }

    [Fact]
    public void Should_produce_valid_json_per_line()
    {
        // Given
        var events = new[] { CreateEvent("TestEvent") };

        // When
        var bytes = JsonlSegmentWriter.Write(events);
        var text = Encoding.UTF8.GetString(bytes);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Then
        var doc = JsonDocument.Parse(lines[0]);
        Assert.NotNull(doc.RootElement.GetProperty("eventPayloadName").GetString());
    }

    [Fact]
    public void Should_return_empty_bytes_for_empty_input()
    {
        // When
        var bytes = JsonlSegmentWriter.Write([]);
        var text = Encoding.UTF8.GetString(bytes);

        // Then
        Assert.Equal(string.Empty, text.Trim());
    }

    [Fact]
    public void Should_preserve_event_payload_name_in_output()
    {
        // Given
        var events = new[] { CreateEvent("OrderCreated") };

        // When
        var bytes = JsonlSegmentWriter.Write(events);
        var text = Encoding.UTF8.GetString(bytes);
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0];
        var doc = JsonDocument.Parse(line);

        // Then
        Assert.Equal("OrderCreated", doc.RootElement.GetProperty("eventPayloadName").GetString());
    }
}
