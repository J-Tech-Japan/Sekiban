using System.Runtime.ExceptionServices;
using ResultBoxes;
namespace Sekiban.Dcb;

/// <summary>
///     Opens a <see cref="ResultBox{TValue}" /> returned by WithResult user code so the exception-based core sees
///     the failure it carries as that same exception (same instance, type and stack).
/// </summary>
internal static class ResultBoxBridge
{
    internal static T Unwrap<T>(ResultBox<T> box) where T : notnull =>
        Unwrap(box, "A WithResult projector, query or handler returned a null ResultBox.");

    /// <summary>Opens a box; a null box fails with an <see cref="InvalidOperationException" /> carrying <paramref name="nullBoxMessage" />.</summary>
    internal static T Unwrap<T>(ResultBox<T> box, string nullBoxMessage) where T : notnull
    {
        if (box is null)
        {
            throw new InvalidOperationException(nullBoxMessage);
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
    // The query execution core has always reported these null results with these messages.
    internal const string NullQueryResultMessage = "HandleQuery returned null result";
    internal const string NullListQueryResultMessage = "Query handler returned invalid result type";

    internal static async Task<T> UnwrapAsync<T>(Task<ResultBox<T>> boxTask) where T : notnull =>
        Unwrap(await boxTask);
}
