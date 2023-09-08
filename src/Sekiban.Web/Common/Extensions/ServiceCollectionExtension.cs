using Microsoft.Extensions.DependencyInjection;
namespace Sekiban.Web.Common.Extensions;

public static class ServiceCollectionExtension
{
    /// <summary>
    ///     Add transient for list of service and implementation type
    /// </summary>
    /// <param name="services"></param>
    /// <param name="dependencies"></param>
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
}
