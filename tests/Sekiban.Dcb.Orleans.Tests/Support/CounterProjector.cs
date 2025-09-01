using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Orleans.Tests;

[GenerateSerializer]
public record CounterProjector([property: Id(0)] int Count) : IMultiProjector<CounterProjector>
{
    public CounterProjector() : this(0) {}
    public static string MultiProjectorVersion => "1.0";
    public static string MultiProjectorName => "snap-proj";
    public static CounterProjector GenerateInitialPayload() => new(0);
    public static ResultBox<CounterProjector> Project(
        CounterProjector payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold) => ResultBox.FromValue(payload with { Count = payload.Count + 1 });

    // Instrumentation for serializer path verification
    public static int SerializeCalls;
    public static int DeserializeCalls;
    public static string Serialize(DcbDomainTypes domainTypes, CounterProjector safePayload)
    {
        SerializeCalls++;
        return System.Text.Json.JsonSerializer.Serialize(new { v = 1, count = safePayload.Count }, domainTypes.JsonSerializerOptions);
    }
    public static CounterProjector Deserialize(DcbDomainTypes domainTypes, string json)
    {
        DeserializeCalls++;
        var obj = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(json, domainTypes.JsonSerializerOptions);
        var count = obj?["count"]?.GetValue<int>() ?? 0;
        return new CounterProjector(count);
    }
}
