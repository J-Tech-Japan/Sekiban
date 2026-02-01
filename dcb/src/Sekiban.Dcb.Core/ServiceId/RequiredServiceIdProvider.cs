namespace Sekiban.Dcb.ServiceId;

/// <summary>
///     Provider that requires explicit ServiceId and throws if missing.
/// </summary>
public sealed class RequiredServiceIdProvider : IServiceIdProvider
{
    public string GetCurrentServiceId() => throw new InvalidOperationException(
        "ServiceId must be explicitly provided in non-HTTP context. Use FixedServiceIdProvider.");
}
