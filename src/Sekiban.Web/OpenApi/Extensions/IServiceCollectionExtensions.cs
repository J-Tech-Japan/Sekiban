using Microsoft.Extensions.DependencyInjection;
using Sekiban.Web.Dependency;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Sekiban.Web.OpenApi.Extensions;
public static class IServiceCollectionExtensions
{
    public static IServiceCollection AddSwaggerGenWithSekibanOpenApiFilter(this IServiceCollection services,
        Action<SwaggerGenOptions>? setupAction = null)
    {
        services.AddSwaggerGen(options =>
        {
            options.CustomSchemaIds(type => SekibanOpenApiParameterGenerator.GenerateCustomSchemaName(type));
            options.SchemaFilter<SekibanOpenApiFilter>();
            options.OperationFilter<SekibanOpenApiFilter>();

            setupAction?.Invoke(options);
        });

        return services;
    }
}
