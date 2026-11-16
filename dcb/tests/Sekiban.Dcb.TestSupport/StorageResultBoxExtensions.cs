using ResultBoxes;

namespace Sekiban.Dcb.TestSupport;

/// <summary>
///     Test-only bridge for assertions written against the 10.x ResultBox storage contracts. The 11 storage contracts
///     are exception based; these helpers turn a storage call's outcome back into a box (an exception becomes a failed
///     box carrying that same exception) so a test can keep asserting <c>IsSuccess</c> / <c>GetException()</c>.
/// </summary>
public static class StorageResultBoxExtensions
{
    /// <summary>Awaits an exception-based storage call and returns its outcome as a box.</summary>
    public static async Task<ResultBox<T>> AsResultBoxAsync<T>(this Task<T> task) where T : notnull
    {
        try
        {
            return ResultBox.FromValue(await task.ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<T>(ex);
        }
    }

    /// <summary>Awaits a storage lookup that returns <c>null</c> for "not found" as a box of an optional value.</summary>
    public static async Task<ResultBox<OptionalValue<T>>> AsOptionalResultBoxAsync<T>(this Task<T?> task)
        where T : class
    {
        try
        {
            var value = await task.ConfigureAwait(false);
            return ResultBox.FromValue(value is null ? OptionalValue<T>.Empty : OptionalValue.FromValue(value));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<OptionalValue<T>>(ex);
        }
    }

    /// <summary>Guards against boxing a call that already returns a box (it is not a storage call).</summary>
    [Obsolete("The call already returns a ResultBox; it is not an exception-based storage call.", true)]
    public static Task<ResultBox<T>> AsResultBoxAsync<T>(this Task<ResultBox<T>> task) where T : notnull => task;
}
