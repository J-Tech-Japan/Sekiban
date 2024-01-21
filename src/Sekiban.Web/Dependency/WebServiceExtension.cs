using Microsoft.AspNetCore.Builder;
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
    /// <param name="builder"></param>
    /// <param name="definition"></param>
    /// <param name="configureMvc"></param>
    /// <returns></returns>
    public static WebApplicationBuilder AddSekibanWeb(
        this WebApplicationBuilder builder,
        IWebDependencyDefinition definition,
        Action<MvcOptions>? configureMvc = null)
    {
        builder.Services.AddSekibanWeb(definition, configureMvc);
        return builder;
    }
    /// <summary>
    ///     Add Sekiban web
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="configureMvc"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static WebApplicationBuilder AddSekibanWeb<T>(this WebApplicationBuilder builder, Action<MvcOptions>? configureMvc = null)
        where T : IWebDependencyDefinition, new()
    {
        builder.Services.AddSekibanWeb<T>(configureMvc);
        return builder;
    }


    /// <summary>
    ///     Add Sekiban web
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="configureWebDependencyDefinition">configure web dependency definition.</param>
    /// <param name="configureMvc"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static WebApplicationBuilder AddSekibanWebFromDomainDependency<T>(
        this WebApplicationBuilder builder,
        Action<GeneralWebDependencyDefinition<T>>? configureWebDependencyDefinition = null,
        Action<MvcOptions>? configureMvc = null) where T : DomainDependencyDefinitionBase, new()
    {
        var definition = new GeneralWebDependencyDefinition<T>();
        if (configureWebDependencyDefinition != null)
        {
            configureWebDependencyDefinition(definition);
        }
        builder.Services.AddSekibanWeb(definition, configureMvc);
        return builder;
    }

    /// <summary>
    ///     Add Sekiban web
    /// </summary>
    /// <param name="services"></param>
    /// <param name="definition"></param>
    /// <param name="configureMvc"></param>
    /// <returns></returns>
    public static IServiceCollection AddSekibanWeb(
        this IServiceCollection services,
        IWebDependencyDefinition definition,
        Action<MvcOptions>? configureMvc = null)
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

                    configureMvc?.Invoke(configure);
                })
            .ConfigureApplicationPartManager(
                setupAction =>
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
        where T : IWebDependencyDefinition, new() =>
        services.AddSekibanWeb(new T(), configureMvcOptions);
    /// <summary>
    ///     Add Sekiban web
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="services"></param>
    /// <param name="configureWebDependencyDefinition"></param>
    /// <param name="configureMvcOptions"></param>
    /// <returns></returns>
    public static IServiceCollection AddSekibanWebFromDomainDependency<TDomainDependency>(
        this IServiceCollection services,
        Action<GeneralWebDependencyDefinition<TDomainDependency>>? configureWebDependencyDefinition = null,
        Action<MvcOptions>? configureMvcOptions = null) where TDomainDependency : DomainDependencyDefinitionBase, new()
    {
        var definition = new GeneralWebDependencyDefinition<TDomainDependency>();
        if (configureWebDependencyDefinition != null)
        {
            configureWebDependencyDefinition(definition);
        }
        return services.AddSekibanWeb(definition, configureMvcOptions);
    }
}
