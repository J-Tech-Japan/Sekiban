using System.Runtime.ExceptionServices;
using ResultBoxes;
namespace Sekiban.Dcb;

/// <summary>
///     Opens a <see cref="ResultBox{TValue}" /> returned by WithResult user code so the exception-based core sees
///     the failure it carries as that same exception (same instance, type and stack).
/// </summary>
internal static class ResultBoxBridge
{
    internal static T Unwrap<T>(ResultBox<T> box) where T : notnull
    {
        if (box is null)
        {
            throw new InvalidOperationException("A WithResult projector, query or handler returned a null ResultBox.");
        }

        if (!box.IsSuccess)
        {
            var failure = box.GetException()
                ?? new InvalidOperationException("A failed ResultBox carried no exception.");
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return box.GetValue();
    }

    /// <summary>Awaits a box returned by WithResult user code and opens it as <see cref="Unwrap{T}" /> does.</summary>
    internal static async Task<T> UnwrapAsync<T>(Task<ResultBox<T>> boxTask) where T : notnull =>
        Unwrap(await boxTask);
}
