namespace Sekiban.EventSourcing.Shared;

public class SekibanDateProducer : ISekibanDateProducer
{
    private static ISekibanDateProducer _registered = new SekibanDateProducer();
    public DateTime Now => DateTime.Now;
    public DateTime UtcNow => DateTime.UtcNow;
    public DateTime Today => DateTime.Today;

    public static ISekibanDateProducer GetRegistered()
    {
        return _registered;
    }
    public static void Register(ISekibanDateProducer sekibanDateProducer)
    {
        _registered = sekibanDateProducer;
    }
}