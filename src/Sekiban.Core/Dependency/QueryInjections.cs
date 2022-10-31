using Microsoft.Extensions.DependencyInjection;
namespace Sekiban.Core.Dependency;

public static class QueryInjections
{

    public static IServiceCollection AddQueries(this IServiceCollection services, params IEnumerable<Type>[] controllerItems)
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
    public static IServiceCollection AddQueriesFromDependencyDefinition(
        this IServiceCollection services,
        IDependencyDefinition dependencyDefinition)
    {
        AddQueries(
            services,
            dependencyDefinition.GetAggregateQueryTypes(),
            dependencyDefinition.GetAggregateListQueryTypes(),
            dependencyDefinition.GetSingleProjectionQueryTypes(),
            dependencyDefinition.GetSingleProjectionListQueryTypes(),
            dependencyDefinition.GetMultiProjectionQueryTypes(),
            dependencyDefinition.GetMultiProjectionListQueryTypes());
        return services;
    }
}
