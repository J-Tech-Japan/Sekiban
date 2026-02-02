namespace Sekiban.Dcb.ServiceId;

/// <summary>
///     Default provider that returns "default" ServiceId for single-tenant deployments.
/// </summary>
public sealed class DefaultServiceIdProvider : IServiceIdProvider
{
    public const string DefaultServiceId = "default";

    public string GetCurrentServiceId() => ServiceIdValidator.NormalizeAndValidate(DefaultServiceId);
}
