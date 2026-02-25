using System.Text;
using System.Text.Json;
using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.ColdEvents;

public static class JsonlSegmentWriter
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static byte[] Write(IReadOnlyList<SerializableEvent> events)
    {
        using var stream = new MemoryStream();
        WriteToStream(events, stream);
        return stream.ToArray();
    }

    private static void WriteToStream(IReadOnlyList<SerializableEvent> events, Stream stream)
    {
        using var writer = new StreamWriter(stream, Utf8NoBom, leaveOpen: true);
        foreach (var e in events)
        {
            var line = JsonSerializer.Serialize(e, ColdEventJsonOptions.Default);
            writer.WriteLine(line);
        }
    }
}
