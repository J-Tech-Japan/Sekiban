using Microsoft.Extensions.DependencyInjection;
using Sekiban.Web.Dependency;
using Swashbuckle.AspNetCore.SwaggerGen;
namespace Sekiban.Web.OpenApi.Extensions;

public static class IServiceCollectionExtensions
{
    public static IServiceCollection AddSwaggerGenWithSekibanOpenApiFilter(this IServiceCollection services,
        Action<SwaggerGenOptions>? configureSwaggerGenOptions = null,
        Func<Type, string>? generateOpenApiSchemaId = null)
    {
        services.AddSwaggerGen(options =>
        {
            options.CustomSchemaIds(generateOpenApiSchemaId ?? SekibanOpenApiSchemaIdGenerator.Generate);

            options.SchemaFilter<SekibanOpenApiFilter>();
            options.OperationFilter<SekibanOpenApiFilter>();

            configureSwaggerGenOptions?.Invoke(options);
        });

        return services;
    }
}
