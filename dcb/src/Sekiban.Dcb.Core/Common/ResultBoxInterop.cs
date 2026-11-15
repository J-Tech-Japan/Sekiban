using System.Runtime.ExceptionServices;
using ResultBoxes;
namespace Sekiban.Dcb.Common;

/// <summary>
///     Bridges between the exception-based Core.Model contracts and the ResultBox flow that the not-yet-ported parts
///     of Core still use internally. A failed box is rethrown as the exception it carries (same instance and stack).
///     Later 11.0 slices remove these call sites together with the internal ResultBox flow.
/// </summary>
internal static class ResultBoxInterop
{
    /// <summary>Returns the value of a successful box, or rethrows the exception a failed box carries.</summary>
    internal static T GetValueOrRethrow<T>(this ResultBox<T> box) where T : notnull
    {
        if (!box.IsSuccess)
        {
            ExceptionDispatchInfo.Capture(box.GetException()).Throw();
        }

        return box.GetValue();
    }

    /// <summary>Awaits the box and returns its value, or rethrows the exception a failed box carries.</summary>
    internal static async Task<T> GetValueOrRethrowAsync<T>(this Task<ResultBox<T>> boxTask) where T : notnull =>
        (await boxTask.ConfigureAwait(false)).GetValueOrRethrow();
}
