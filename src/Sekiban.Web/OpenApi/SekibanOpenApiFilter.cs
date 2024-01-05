using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Sekiban.Web.OpenApi.Extensions;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
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
            GenerateSchemaForEnum(context.Type, schema);
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

    private static OpenApiSchema GenerateSchemaForEnum(Type propertyType, OpenApiSchema? schema = default)
    {
        var baseType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        schema ??= new OpenApiSchema();
        schema.Type = baseType.Name;
        schema.Nullable = Nullable.GetUnderlyingType(propertyType) is not null;

        var enums = new OpenApiArray();
        enums.AddRange(Enum.GetValues(baseType).Cast<Enum>().Select(enm => new OpenApiString(enm.ToString())));
        schema.Enum = enums;

        var displayNames = Enum.GetValues(baseType)
            .Cast<Enum>()
            .Select(
                enm => enm.GetType().GetMember(enm.ToString()) is { } members && members.FirstOrDefault() is { } member
                    ? member.GetCustomAttribute<DisplayAttribute>()?.Name ?? member.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName
                    : null)
            .ToList();
        if (displayNames.Any(a => !string.IsNullOrEmpty(a)))
        {
            var enumVarNames = new OpenApiArray();
            enumVarNames.AddRange(displayNames.Select(s => new OpenApiString(s)));
            schema.Extensions.Add("x-enum-varnames", enumVarNames);
        }

        var descriptions = Enum.GetValues(baseType)
            .Cast<Enum>()
            .Select(
                enm => enm.GetType().GetMember(enm.ToString()) is { } members && members.FirstOrDefault() is { } member
                    ? member.GetCustomAttribute<DisplayAttribute>()?.Description ?? member.GetCustomAttribute<DescriptionAttribute>()?.Description
                    : null)
            .ToList();
        if (descriptions.Any(a => !string.IsNullOrEmpty(a)))
        {
            var enumDescriptions = new OpenApiArray();
            enumDescriptions.AddRange(descriptions.Select(s => new OpenApiString(s)));
            schema.Extensions.Add("x-enum-descriptions", enumDescriptions);
        }

        return schema;
    }
}
