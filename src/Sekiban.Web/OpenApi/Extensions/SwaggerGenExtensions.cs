using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.SwaggerGen;
namespace Sekiban.Web.OpenApi.Extensions;

public static class SwaggerGenExtensions
{
    [Obsolete($"{nameof(AddSekibanSwaggerGen)} is obsolete. Use the IServiceCollection.AddSwaggerGenWithSekibanOpenApiFilter method instead.")]
    public static SwaggerGenOptions AddSekibanSwaggerGen(this SwaggerGenOptions options)
    {
        options.CustomSchemaIds(SekibanOpenApiParameterGenerator.GenerateCustomSchemaName);
        options.SchemaFilter<SekibanOpenApiFilter>();
        options.OperationFilter<SekibanOpenApiFilter>();
        return options;
    }
}
