namespace Sekiban.Dcb.ServiceId;

/// <summary>
///     Provides the current ServiceId for tenant isolation.
/// </summary>
public interface IServiceIdProvider
{
    /// <summary>
    ///     Gets the current ServiceId.
    /// </summary>
    /// <returns>Normalized ServiceId. Never null or empty.</returns>
    string GetCurrentServiceId();
}
