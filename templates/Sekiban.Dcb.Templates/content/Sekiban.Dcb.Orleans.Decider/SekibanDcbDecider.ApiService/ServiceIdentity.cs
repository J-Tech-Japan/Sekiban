using Microsoft.Extensions.DependencyInjection.Extensions;
using Sekiban.Dcb.ServiceId;

namespace SekibanDcbDecider.ApiService;

public static class ServiceIdentity
{
    public static string Resolve(IConfiguration configuration, string defaultServiceId) =>
        ServiceIdValidator.NormalizeAndValidate(configuration["Sekiban:ServiceId"] ?? defaultServiceId);

    public static void Register(IServiceCollection services, string serviceId)
    {
        services.RemoveAll<IServiceIdProvider>();
        services.AddSingleton<IServiceIdProvider>(new FixedServiceIdProvider(serviceId));
    }
}
