using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Dependency;
using Sekiban.Web.Common;
namespace Sekiban.Web.Dependency;

public static class WebServiceExtension
{
    /// <summary>
    ///     Add Sekiban web
    /// </summary>
    /// <param name="services"></param>
    /// <param name="definition"></param>
    /// <returns></returns>
    public static IServiceCollection AddSekibanWeb(this IServiceCollection services, IWebDependencyDefinition definition)
    {
        definition.Define();
        services.AddSingleton(definition);
        services.AddControllers(
                configure =>
                {
                    configure.Conventions.Add(new SekibanControllerRouteConvention(definition));
                    configure.ModelValidatorProviders.Clear();
                    if (definition.ShouldAddExceptionFilter)
                    {
                        configure.Filters.Add<SimpleExceptionFilter>();
                    }
                })
            .ConfigureApplicationPartManager(setupAction => { setupAction.FeatureProviders.Add(new SekibanControllerFeatureProvider(definition)); });
        services.AddQueriesFromDependencyDefinition(definition);
        services.AddQueries(definition.GetSimpleAggregateListQueryTypes(), definition.GetSimpleSingleProjectionListQueryTypes());
        return services;
    }
}
