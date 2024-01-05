using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
namespace Sekiban.Web.OpenApi;

public static class SekibanOpenApiParameterGenerator
{
    public static string GenerateCustomSchemaName(Type type) => GetSekibanSchemeName(type);

    private static string GetSekibanSchemeName(Type type, string prefix = "")
    {
        var regular = GetRegularNameWithReplacedSymbol(type);
        if (type.IsGenericType)
        {
            return type.GenericTypeArguments.ToList()
                .Aggregate(string.IsNullOrEmpty(prefix) ? regular : $"{prefix}_{regular}", (s, type1) => GetSekibanSchemeName(type1, s));
        }
        return string.IsNullOrEmpty(prefix) ? regular : $"{prefix}_{regular}";
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

    private static string GetRegularName(Type type)
    {
        if (type.IsGenericType)
        {
            return type.GetGenericTypeDefinition().Name.Split("`").FirstOrDefault() ?? string.Empty;
        }
        return type.FullName!.Contains('+') ? type.FullName.Split(".").LastOrDefault() ?? string.Empty : type.Name;
    }
}
