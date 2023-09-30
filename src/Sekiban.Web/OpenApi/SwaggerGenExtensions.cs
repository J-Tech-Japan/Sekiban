using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.SwaggerGen;
namespace Sekiban.Web.OpenApi;

public static class SwaggerGenExtensions
{
    public static SwaggerGenOptions AddSekibanSwaggerGen(this SwaggerGenOptions options)
    {
        options.CustomSchemaIds(SekibanOpenApiParameterGenerator.GenerateCustomSchemaName);
        options.SchemaFilter<SekibanOpenApiFilter>();
        options.OperationFilter<SekibanOpenApiFilter>();
        return options;
    }
}
