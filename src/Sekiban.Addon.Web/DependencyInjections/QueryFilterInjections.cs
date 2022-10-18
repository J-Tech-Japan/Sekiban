using Microsoft.Extensions.DependencyInjection;
using Sekiban.Addon.Web.Common;
namespace Sekiban.Addon.Web.DependencyInjections;

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
