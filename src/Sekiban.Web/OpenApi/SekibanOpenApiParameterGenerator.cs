using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
namespace Sekiban.Web.OpenApi;

public static class SekibanOpenApiParameterGenerator
{
    private static readonly NullabilityInfoContext _nullabilityContext = new();

    private static string GetRegularName(Type type)
    {
        if (type.IsGenericType)
        {
            return type.GetGenericTypeDefinition().Name.Split("`").FirstOrDefault() ?? string.Empty;
        }
        return type.FullName!.Contains('+') ? type.FullName.Split(".").LastOrDefault() ?? string.Empty : type.Name ?? string.Empty;
    }

    private static string GetRegularNameWithReplacedSymbol(Type type) =>
        GetRegularName(type)
            .Replace("+", "_")
            .Replace("`", string.Empty)
            .Replace("[", string.Empty)
            .Replace("]", string.Empty)
            .Replace(" ", string.Empty)
            .Replace(",", string.Empty)
            .Replace(".", string.Empty)
            .Replace("=", string.Empty);

    public static string GetSekibanSchemeName(Type type, string prefix = "")
    {
        var regular = GetRegularNameWithReplacedSymbol(type);
        if (type.IsGenericType)
        {
            return type.GenericTypeArguments.ToList()
                .Aggregate(string.IsNullOrEmpty(prefix) ? regular : $"{prefix}_{regular}", (s, type1) => GetSekibanSchemeName(type1, s));
        }
        regular = regular.Replace("+", "_");
        return string.IsNullOrEmpty(prefix) ? regular : $"{prefix}_{regular}";
    }

    public static string GenerateCustomSchemaName(Type type) => GetSekibanSchemeName(type);

    public static OpenApiSchema GenerateSchemaForEnum(Type propertyType, OpenApiSchema? schema = default)
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
                enm => enm.GetType().GetMember(enm.ToString()) is MemberInfo[] members && members.FirstOrDefault() is MemberInfo member
                    ? member.GetCustomAttribute<DisplayAttribute>()?.Name ?? member.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName
                    : null);
        if (displayNames.Any(a => !string.IsNullOrEmpty(a)))
        {
            var enumVarNames = new OpenApiArray();
            enumVarNames.AddRange(displayNames.Select(s => new OpenApiString(s)));
            schema.Extensions.Add("x-enum-varnames", enumVarNames);
        }

        var descriptions = Enum.GetValues(baseType)
            .Cast<Enum>()
            .Select(
                enm => enm.GetType().GetMember(enm.ToString()) is MemberInfo[] members && members.FirstOrDefault() is MemberInfo member
                    ? member.GetCustomAttribute<DisplayAttribute>()?.Description ?? member.GetCustomAttribute<DescriptionAttribute>()?.Description
                    : null);
        if (descriptions.Any(a => !string.IsNullOrEmpty(a)))
        {
            var enumDescriptions = new OpenApiArray();
            enumDescriptions.AddRange(descriptions.Select(s => new OpenApiString(s)));
            schema.Extensions.Add("x-enum-descriptions", enumDescriptions);
        }

        return schema;
    }
}
