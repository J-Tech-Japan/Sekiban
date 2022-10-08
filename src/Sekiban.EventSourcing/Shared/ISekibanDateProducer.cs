namespace Sekiban.EventSourcing.Shared;

public interface ISekibanDateProducer
{
    public DateTime Now { get; }
    public DateTime UtcNow { get; }
    public DateTime Today { get; }
}
