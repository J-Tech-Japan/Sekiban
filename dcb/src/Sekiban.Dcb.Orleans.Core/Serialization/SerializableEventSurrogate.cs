namespace Sekiban.Dcb.Orleans.Serialization;

/// <summary>
///     Surrogate for serializing Sekiban.Dcb.Events.SerializableEvent in Orleans streams
/// </summary>
[GenerateSerializer]
public struct SerializableEventSurrogate
{
    [Id(0)]
    public byte[] Payload { get; set; }
    [Id(1)]
    public string SortableUniqueIdValue { get; set; }
    [Id(2)]
    public Guid Id { get; set; }
    [Id(3)]
    public string CausationId { get; set; }
    [Id(4)]
    public string CorrelationId { get; set; }
    [Id(5)]
    public string ExecutedUser { get; set; }
    [Id(6)]
    public List<string> Tags { get; set; }
    [Id(7)]
    public string EventPayloadName { get; set; }
}
