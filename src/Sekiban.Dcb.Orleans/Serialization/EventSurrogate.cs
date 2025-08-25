namespace Sekiban.Dcb.Orleans.Serialization;

/// <summary>
///     Surrogate for serializing Sekiban.Dcb.Events.Event in Orleans streams
/// </summary>
[GenerateSerializer]
public struct EventSurrogate
{
    [Id(0)]
    public string PayloadJson { get; set; }
    [Id(1)]
    public string PayloadTypeName { get; set; }
    [Id(2)]
    public string SortableUniqueIdValue { get; set; }
    [Id(3)]
    public string EventType { get; set; }
    [Id(4)]
    public Guid Id { get; set; }
    [Id(5)]
    public string CausationId { get; set; }
    [Id(6)]
    public string CorrelationId { get; set; }
    [Id(7)]
    public string ExecutedUser { get; set; }
    [Id(8)]
    public List<string> Tags { get; set; }
}
