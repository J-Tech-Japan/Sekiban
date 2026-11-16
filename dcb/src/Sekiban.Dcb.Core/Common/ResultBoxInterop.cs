using System.Runtime.ExceptionServices;
using ResultBoxes;
namespace Sekiban.Dcb.Common;

/// <summary>
///     Bridges between the exception-based Core.Model and storage contracts and the ResultBox flow that the not-yet-ported parts
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

    /// <summary>
    ///     Awaits an exception-based call and returns its outcome as a box, the same way <c>ResultBox.WrapTry</c> does:
    ///     every exception, cancellation included, becomes a failed box carrying that exception.
    /// </summary>
    internal static async Task<ResultBox<T>> ToResultBoxAsync<T>(this Task<T> task) where T : notnull
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

    /// <summary>
    ///     Like <see cref="ToResultBoxAsync{T}" />, but <see cref="OperationCanceledException" /> propagates instead of
    ///     becoming a failed box. Used for streaming reads whose contract always propagated cancellation.
    /// </summary>
    internal static async Task<ResultBox<T>> ToResultBoxPropagatingCancellationAsync<T>(this Task<T> task)
        where T : notnull
    {
        try
        {
            return ResultBox.FromValue(await task.ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ResultBox.Error<T>(ex);
        }
    }

    /// <summary>
    ///     Awaits a lookup that returns <c>null</c> for "not found" and returns it as a box of
    ///     <see cref="OptionalValue{T}" />; exceptions become a failed box.
    /// </summary>
    internal static async Task<ResultBox<OptionalValue<T>>> ToOptionalResultBoxAsync<T>(this Task<T?> task)
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
}
