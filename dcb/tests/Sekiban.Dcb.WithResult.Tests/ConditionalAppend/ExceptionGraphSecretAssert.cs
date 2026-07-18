using Xunit;
namespace Sekiban.Dcb.Tests.ConditionalAppend;

/// <summary>
///     Recursively asserts that no sentinel secret appears anywhere in the externally observable exception graph — every
///     node's Message, Data (keys and values), StackTrace, and ToString, walked through the whole InnerException chain.
/// </summary>
internal static class ExceptionGraphSecretAssert
{
    public static void ContainsNoneOf(Exception exception, params string[] sentinels)
    {
        for (var current = (Exception?)exception; current is not null; current = current.InnerException)
        {
            var surfaces = new[]
            {
                current.Message ?? string.Empty,
                current.ToString() ?? string.Empty,
                current.StackTrace ?? string.Empty
            };

            foreach (var sentinel in sentinels)
            {
                foreach (var surface in surfaces)
                {
                    Assert.DoesNotContain(sentinel, surface, StringComparison.Ordinal);
                }

                foreach (System.Collections.DictionaryEntry entry in current.Data)
                {
                    Assert.DoesNotContain(sentinel, entry.Key?.ToString() ?? string.Empty, StringComparison.Ordinal);
                    Assert.DoesNotContain(sentinel, entry.Value?.ToString() ?? string.Empty, StringComparison.Ordinal);
                }
            }
        }
    }
}
