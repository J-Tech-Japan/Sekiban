using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.SwaggerGen;
namespace Sekiban.Web.OpenApi.Extensions;

public static class SwaggerGenExtensions
{
    [Obsolete($"{nameof(AddSekibanSwaggerGen)} is obsolete. Use the {nameof(ConfigureForSekiban)} method instead.")]
    public static SwaggerGenOptions AddSekibanSwaggerGen(this SwaggerGenOptions options)
    {
        options.CustomSchemaIds(SekibanOpenApiSchemaIdGenerator.Generate);
        options.SchemaFilter<SekibanOpenApiFilter>();
        options.OperationFilter<SekibanOpenApiFilter>();
        return options;
    }

    public static void ConfigureForSekiban(this SwaggerGenOptions options, Func<Type, string>? generateSchemaId = null)
    {
        options.CustomSchemaIds(generateSchemaId ?? SekibanOpenApiSchemaIdGenerator.Generate);

        options.SchemaFilter<SekibanOpenApiFilter>();
        options.OperationFilter<SekibanOpenApiFilter>();
    }
}
