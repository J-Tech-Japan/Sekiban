using Microsoft.Extensions.DependencyInjection;
using Sekiban.Addon.Web.Common;
using Sekiban.Core.Dependency;
namespace Sekiban.Addon.Web.Dependency;

public static class WebServiceExtension
{
    public static IServiceCollection AddSekibanWebAddon(
        this IServiceCollection services,
        IWebDependencyDefinition definition)
    {
        services.AddSingleton(definition);
        services.AddControllers(
                configure =>
                {
                    configure.Conventions.Add(new SekibanControllerRouteConvention(definition));
                    configure.ModelValidatorProviders.Clear();
                })
            .ConfigureApplicationPartManager(
                setupAction => { setupAction.FeatureProviders.Add(new SekibanControllerFeatureProvider(definition)); });
        services.AddQueriesFromDependencyDefinition(definition);
        services.AddQueries(definition.GetSimpleAggregateListQueryTypes(), definition.GetSimpleSingleProjectionListQueryTypes());
        return services;
    }
}
