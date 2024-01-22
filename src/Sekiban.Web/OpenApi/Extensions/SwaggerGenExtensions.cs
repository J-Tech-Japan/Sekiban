using Jtechs.OpenApi.AspNetCore.Swashbuckle;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.SwaggerGen;
namespace Sekiban.Web.OpenApi.Extensions;

public static class SwaggerGenExtensions
{
    [Obsolete($"{nameof(AddSekibanSwaggerGen)} is obsolete. Use the {nameof(ConfigureForSekibanWeb)} method instead.")]
    public static SwaggerGenOptions AddSekibanSwaggerGen(this SwaggerGenOptions options)
    {
        options.UseSekibanSchemaId();
        options.SchemaFilter<SekibanOpenApiFilter>();
        return options;
    }

    public static void ConfigureForSekibanWeb(this SwaggerGenOptions options)
    {
        options.UseSekibanSchemaId();
        options.AddJtechsOpenApiFilters();
    }

    public static void UseSekibanSchemaId(this SwaggerGenOptions options)
    {
        options.CustomSchemaIds(SekibanOpenApiSchemaIdGenerator.Generate);
    }
}
