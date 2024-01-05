using Microsoft.Extensions.DependencyInjection;
using Sekiban.Web.Dependency;
using Swashbuckle.AspNetCore.SwaggerGen;
namespace Sekiban.Web.OpenApi.Extensions;

public static class IServiceCollectionExtensions
{
    public static IServiceCollection AddSwaggerGenWithSekibanOpenApiFilter(this IServiceCollection services,
        Action<SwaggerGenOptions>? setupSwaggerGenAction = null,
        Func<Type, string>? openApiCustomSchemaIdSelector = null)
    {
        services.AddSwaggerGen(options =>
        {
            options.CustomSchemaIds(openApiCustomSchemaIdSelector ?? SekibanOpenApiParameterGenerator.GenerateCustomSchemaName);

            options.SchemaFilter<SekibanOpenApiFilter>();
            options.OperationFilter<SekibanOpenApiFilter>();

            setupSwaggerGenAction?.Invoke(options);
        });

        return services;
    }
}
