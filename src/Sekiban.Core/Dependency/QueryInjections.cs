using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Types;
namespace Sekiban.Core.Dependency;

public static class QueryInjections
{
    public static IServiceCollection AddQueries(
        this IServiceCollection services,
        params IEnumerable<Type>[] controllerItems)
    {
        foreach (var types in controllerItems)
        foreach (var type in types)
        {
            services.AddTransient(type);
            if (type.IsAggregateListQueryType())
            {
                var input = type.GetParamTypeFromAggregateListQueryType();
                var output = type.GetResponseTypeFromAggregateListQueryType();
                services.AddTransient(typeof(IListHandlerCommon<,>).MakeGenericType(input, output), type);
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
