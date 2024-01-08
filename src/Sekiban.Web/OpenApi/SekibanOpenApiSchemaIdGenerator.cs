namespace Sekiban.Web.OpenApi;

public static class SekibanOpenApiSchemaIdGenerator
{
    public static string Generate(Type type) => GenerateSchemaId(type, string.Empty);

    private static string GenerateSchemaId(Type type, string prefix = "")
    {
        var regular = GetRegularNameWithReplacedSymbol(type);
        return type.IsGenericType
            ? type.GenericTypeArguments.ToList()
                .Aggregate(string.IsNullOrEmpty(prefix) ? regular : $"{prefix}_{regular}", (s, type1) => GenerateSchemaId(type1, s))
            : string.IsNullOrEmpty(prefix) ? regular : $"{prefix}_{regular}";
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
        return type.IsGenericType
            ? type.GetGenericTypeDefinition().Name.Split("`").FirstOrDefault() ?? string.Empty
            : type.FullName!.Contains('+') ? type.FullName.Split(".").LastOrDefault() ?? string.Empty : type.Name;
    }
}
