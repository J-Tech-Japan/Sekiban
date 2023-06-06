using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Types;
namespace Sekiban.Core.Dependency;

/// <summary>
///     System use this class to resolve query dependencies
///     Application developers does not need to use this class directly
/// </summary>
public static class QueryInjections
{
    public static IServiceCollection AddQueries(this IServiceCollection services, params IEnumerable<Type>[] controllerItems)
    {
        foreach (var types in controllerItems)
        foreach (var type in types)
        {
            services.AddTransient(type);
            if (type.IsAggregateListQueryType())
            {
                var input = type.GetParamTypeFromAggregateListQueryType();
                var output = type.GetResponseTypeFromAggregateListQueryType();
                services.AddTransient(typeof(IListQueryHandlerCommon<,>).MakeGenericType(input, output), type);
            }
            if (type.IsSingleProjectionListQueryType())
            {
                var input = type.GetParamTypeFromSingleProjectionListQueryType();
                var output = type.GetResponseTypeFromSingleProjectionListQueryType();
                services.AddTransient(typeof(IListQueryHandlerCommon<,>).MakeGenericType(input, output), type);
            }
            if (type.IsMultiProjectionListQueryType())
            {
                var input = type.GetParamTypeFromMultiProjectionListQueryType();
                var output = type.GetResponseTypeFromMultiProjectionListQueryType();
                services.AddTransient(typeof(IListQueryHandlerCommon<,>).MakeGenericType(input, output), type);
            }

            if (type.IsAggregateQueryType())
            {
                var input = type.GetParamTypeFromAggregateQueryType();
                var output = type.GetResponseTypeFromAggregateQueryType();
                services.AddTransient(typeof(IQueryHandlerCommon<,>).MakeGenericType(input, output), type);
            }
            if (type.IsSingleProjectionQueryType())
            {
                var input = type.GetParamTypeFromSingleProjectionQueryType();
                var output = type.GetResponseTypeFromSingleProjectionQueryType();
                services.AddTransient(typeof(IQueryHandlerCommon<,>).MakeGenericType(input, output), type);
            }
            if (type.IsMultiProjectionQueryType())
            {
                var input = type.GetParamTypeFromMultiProjectionQueryType();
                var output = type.GetResponseTypeFromMultiProjectionQueryType();
                services.AddTransient(typeof(IQueryHandlerCommon<,>).MakeGenericType(input, output), type);
            }

        }
        return services;
    }

    public static IServiceCollection AddQueriesFromDependencyDefinition(this IServiceCollection services, IQueryDefinition dependencyDefinition)
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
