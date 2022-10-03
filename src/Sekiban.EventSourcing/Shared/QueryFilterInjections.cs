using Microsoft.Extensions.DependencyInjection;
namespace Sekiban.EventSourcing.Shared;

public static class QueryFilterInjections
{

    public static IServiceCollection AddQueryFilters(this IServiceCollection services, params IEnumerable<Type>[] controllerItems)
    {
        foreach (var types in controllerItems)
        {
            foreach (var type in types)
            {
                services.AddTransient(type);
            }
        }
        return services;
    }
}
