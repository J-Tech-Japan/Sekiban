using Microsoft.Extensions.DependencyInjection;
namespace Sekiban.Addon.Tenant.Extensions;

public static class IServiceCollectionExtensions
{
    public static void AddTransient(this IServiceCollection services, IEnumerable<(Type serviceType, Type? implementationType)> dependencies)
    {
        foreach (var (serviceType, implementationType) in dependencies)
        {
            if (implementationType is null)
            {
                services.AddTransient(serviceType);
            } else
            {
                services.AddTransient(serviceType, implementationType);
            }
        }
    }

    public static void AddScoped(this IServiceCollection services, IEnumerable<(Type serviceType, Type? implementationType)> dependencies)
    {
        foreach (var (serviceType, implementationType) in dependencies)
        {
            if (implementationType is null)
            {
                services.AddScoped(serviceType);
            } else
            {
                services.AddScoped(serviceType, implementationType);
            }
        }
    }

    public static void AddSingleton(this IServiceCollection services, IEnumerable<(Type serviceType, Type? implementationType)> dependencies)
    {
        foreach (var (serviceType, implementationType) in dependencies)
        {
            if (implementationType is null)
            {
                services.AddSingleton(serviceType);
            } else
            {
                services.AddSingleton(serviceType, implementationType);
            }
        }
    }
}
