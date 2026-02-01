namespace Sekiban.Dcb.ServiceId;

/// <summary>
///     Provider that returns a fixed ServiceId.
/// </summary>
public sealed class FixedServiceIdProvider : IServiceIdProvider
{
    private readonly string _serviceId;

    public FixedServiceIdProvider(string serviceId)
    {
        _serviceId = ServiceIdValidator.NormalizeAndValidate(serviceId);
    }

    public string GetCurrentServiceId() => _serviceId;
}
