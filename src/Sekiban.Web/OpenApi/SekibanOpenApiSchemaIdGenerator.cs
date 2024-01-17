using System.ComponentModel.DataAnnotations;

namespace Sekiban.Web.OpenApi;

public static class SekibanOpenApiSchemaIdGenerator
{
    public static string Generate(Type type) => GenerateSchemaId(type, string.Empty);

    private static string GenerateSchemaId(Type type, string prefix = "")
    {
        return (type.IsGenericType, string.IsNullOrEmpty(prefix), GetRegularNameWithReplacedSymbol(type)) switch
        {
            (false, true, var r) => r
            ,
            (false, false, var r) => $"{prefix}_{r}"
            ,
            (true, true, var r) => type.GenericTypeArguments.ToList().Aggregate(r, (s, type1) => GenerateSchemaId(type1, s))
            ,
            (true, false, var r) => type.GenericTypeArguments.ToList().Aggregate($"{prefix}_{r}", (s, type1) => GenerateSchemaId(type1, s))
            ,
        };
    }

    private static string GetRegularNameWithReplacedSymbol(Type type)
    {
        return GetRegularName(type)
            .Replace("+", "_")
            .Replace("`", string.Empty)
            .Replace("[", string.Empty)
            .Replace("]", string.Empty)
            .Replace(" ", string.Empty)
            .Replace(",", string.Empty)
            .Replace(".", string.Empty)
            .Replace("=", string.Empty);
    }

    private static string GetRegularName(Type type)
    {
        return type switch
        {
            { IsGenericType: true } t => t.GetGenericTypeDefinition().Name.Split("`").FirstOrDefault()
            ,
            var t when t.FullName!.Contains('+') => t.FullName.Split(".").LastOrDefault()
            ,
            _ => type.Name
            ,
        } ?? string.Empty;
    }
}
