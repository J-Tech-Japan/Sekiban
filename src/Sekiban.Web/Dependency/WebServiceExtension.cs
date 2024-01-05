using Microsoft.AspNetCore.Mvc;
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
    /// <param name="configureMvcOptions"></param>
    /// <returns></returns>
    public static IServiceCollection AddSekibanWeb(this IServiceCollection services, IWebDependencyDefinition definition,
        Action<MvcOptions>? configureMvcOptions = null)
    {
        definition.Define();
        services.AddSingleton(definition);

        services
            .AddControllers(configure =>
            {
                configure.Conventions.Add(new SekibanControllerRouteConvention(definition));
                configure.ModelValidatorProviders.Clear();
                if (definition.ShouldAddExceptionFilter)
                {
                    configure.Filters.Add<SimpleExceptionFilter>();
                }

                configureMvcOptions?.Invoke(configure);
            })
            .ConfigureApplicationPartManager(setupAction =>
            {
                setupAction.FeatureProviders.Add(new SekibanControllerFeatureProvider(definition));
            });

        services.AddQueriesFromDependencyDefinition(definition);
        services.AddQueries(definition.GetSimpleAggregateListQueryTypes(), definition.GetSimpleSingleProjectionListQueryTypes());

        return services;
    }

    /// <summary>
    ///     Add Sekiban web
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="services"></param>
    /// <param name="configureMvcOptions"></param>
    /// <returns></returns>
    public static IServiceCollection AddSekibanWeb<T>(this IServiceCollection services, Action<MvcOptions>? configureMvcOptions = null)
        where T : IWebDependencyDefinition, new() => services.AddSekibanWeb(new T(), configureMvcOptions);
}
