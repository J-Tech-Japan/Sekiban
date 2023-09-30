using System.Text;
using System.Text.RegularExpressions;
namespace Sekiban.Web.OpenApi;

public static partial class StringExtensions
{
    [GeneratedRegex(@"[\x00-\x2F\x3A-\x40\x5B-\x60\x7B-\x7F]+")]
    private static partial Regex AsciiSymbolAndControlCharsRegex();

    public static bool IsAsciiString(this string s)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // Shift-JISを扱うための呼び出し
        var enc = Encoding.GetEncoding("Shift_JIS");
        return enc.GetByteCount(s) == s.Length;
    }

    public static string ToUpperFirstChar(this string s) => s[..1].ToUpper() + s[1..s.Length];

    public static string ToLowerFirstChar(this string s) => s[..1].ToLower() + s[1..s.Length];

    public static string ReplaceAsciiSymbolAndControlChars(this string s, string replacement) =>
        AsciiSymbolAndControlCharsRegex().Replace(s, replacement);
}
