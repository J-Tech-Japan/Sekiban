namespace Sekiban.Core.Shared;

/// <summary>
///     Date Producer Interface.
///     Use this interface instead of DateTime, DateTime.UtcNow, DateTime.Today.
///     This is supporting for testing mock datetime.
/// </summary>
public interface ISekibanDateProducer
{
    public DateTime Now { get; }
    public DateTime UtcNow { get; }
    public DateTime Today { get; }
}
