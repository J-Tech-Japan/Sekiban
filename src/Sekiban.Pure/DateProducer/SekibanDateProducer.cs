namespace Sekiban.Core.Shared;

/// <summary>
///     Sekiban Date Producer.
/// </summary>
public class SekibanDateProducer : ISekibanDateProducer
{
    private static ISekibanDateProducer _registered = new SekibanDateProducer();
    public DateTime Now => DateTime.Now;
    public DateTime UtcNow => DateTime.UtcNow;
    public DateTime Today => DateTime.Today;

    public static ISekibanDateProducer GetRegistered() => _registered;

    public static void Register(ISekibanDateProducer sekibanDateProducer)
    {
        _registered = sekibanDateProducer;
    }
}
