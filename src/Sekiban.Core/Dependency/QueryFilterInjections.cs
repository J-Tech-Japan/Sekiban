using Microsoft.Extensions.DependencyInjection;
namespace Sekiban.Core.Dependency;

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
    public static IServiceCollection AddQueryFiltersFromDependencyDefinition(
        this IServiceCollection services,
        IDependencyDefinition dependencyDefinition)
    {
        AddQueryFilters(
            services,
            dependencyDefinition.GetAggregateQueryFilterTypes(),
            dependencyDefinition.GetAggregateListQueryFilterTypes(),
            dependencyDefinition.GetSingleAggregateProjectionQueryFilterTypes(),
            dependencyDefinition.GetSingleAggregateProjectionListQueryFilterTypes(),
            dependencyDefinition.GetProjectionQueryFilterTypes(),
            dependencyDefinition.GetProjectionListQueryFilterTypes());
        return services;
    }
}