namespace Sekiban.Dcb.Events;

[Serializable]
public record SerializableDomainEventDocument
{
    public Guid Id { get; init; } = Guid.Empty;
    public string SortableUniqueId { get; init; } = string.Empty;
    public string PayloadTypeName { get; init; } = string.Empty;
    public DateTime TimeStamp { get; init; }
    public string CausationId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public string ExecutedUser { get; init; } = string.Empty;
    
    public byte[] CompressedPayloadJson { get; init; } = Array.Empty<byte>();
    
    public string PayloadAssemblyVersion { get; init; } = string.Empty;
    
    public SerializableDomainEventDocument() { }

    public DomainEventMetadata GetEventMetadata() => new(CausationId, CorrelationId, ExecutedUser);
}