using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
namespace Sekiban.Web.OpenApi;

public class SekibanOpenApiFilter : ISchemaFilter, IOperationFilter
{

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation.RequestBody is not null && (operation.RequestBody.Content is null || !operation.RequestBody.Content.Any()))
        {
            operation.RequestBody = null;
        }
    }
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type is not null && context.Type.IsEnum)
        {
            SekibanOpenApiParameterGenerator.GenerateSchemaForEnum(context.Type, schema);
        }

        (schema.Title, schema.Description) = context switch
        {
            { ParameterInfo: var pi } when pi is not null && pi.CustomAttributes.Any() => (pi.GetDisplayName() ?? schema.Title,
                pi.GetDescription() ?? schema.Description),

            { MemberInfo: var mi } when mi is not null && mi.CustomAttributes.Any() => (mi.GetDisplayName() ?? schema.Title,
                mi.GetDescription() ?? schema.Description),

            { Type: var tp } when tp is not null && tp.CustomAttributes.Any() => (tp.GetDisplayName() ?? schema.Title,
                tp.GetDescription() ?? schema.Description),

            _ => (schema.Title, schema.Description)
        };
    }
}
