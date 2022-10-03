using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.WebHelper.Common;
namespace Sekiban.EventSourcing.WebHelper.DependencyInjections;

public static class QueryFilterInjections
{
    public static IServiceCollection AddQueryFilterFromSekibanControllerItems(
        this IServiceCollection services,
        SekibanControllerItems controllerItems)
    {
        foreach (var type in controllerItems.ProjectionQueryFilters)
        {
            services.AddTransient(type);
        }
        foreach (var type in controllerItems.ProjectionListQueryFilters)
        {
            services.AddTransient(type);
        }
        foreach (var type in controllerItems.AggregateListQueryFilters)
        {
            services.AddTransient(type);
        }
        foreach (var type in controllerItems.SingleAggregateProjectionListQueryFilters)
        {
            services.AddTransient(type);
        }
        return services;
    }
}
