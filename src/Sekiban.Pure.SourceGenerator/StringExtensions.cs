namespace Sekiban.Pure.SourceGenerator;

public static class StringExtensions
{
    public static string ToCamelCase(this string str)
    {
        if (string.IsNullOrEmpty(str))
            return str;
        return char.ToLowerInvariant(str[0]) + str.Substring(1);
    }
}