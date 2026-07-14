using ResultBoxes;
using System.Runtime.ExceptionServices;
namespace Sekiban.Dcb.Boundaries;

/// <summary>
///     What a boundary was doing, so that a failure can say so.
/// </summary>
/// <param name="Operation">The boundary the caller invoked, e.g. <c>ISekibanExecutor.QueryAsync</c>.</param>
/// <param name="Target">The command / query / projector / tag it was working on, when there is one.</param>
internal readonly record struct BoundaryContext(string Operation, string? Target = null)
{
    /// <summary>e.g. <c>ISekibanExecutor.QueryAsync (GetStudentQuery)</c>.</summary>
    public string Describe() => string.IsNullOrWhiteSpace(Target) ? Operation : $"{Operation} ({Target})";
}

/// <summary>
///     Opens a <see cref="ResultBox{T}" /> at a WithoutResult boundary, and says something useful when it cannot.
///     This exists because <c>UnwrapBox()</c> does not. Its behaviour depends on the shape of what is inside:
///     <list type="bullet">
///         <item>
///             <description>
///                 a failed box whose <c>T</c> is a REFERENCE type rethrows the carried failure — fine, and the
///                 behaviour every WithoutResult caller already depends on;
///             </description>
///         </item>
///         <item>
///             <description>
///                 a failed box whose <c>T</c> is a VALUE type returns <c>default</c> and throws nothing at all,
///                 so the failure becomes a <c>false</c> or a <c>0</c> that the caller trusts. That is not a
///                 diagnostic wart, it is a wrong answer: <c>ICommandContext.TagExistsAsync</c> returns
///                 <c>bool</c>, so a storage failure looked exactly like "the tag does not exist";
///             </description>
///         </item>
///         <item>
///             <description>
///                 a null box is a bare <see cref="NullReferenceException" /> with no message, which is the
///                 experience reported in issue #1045.
///             </description>
///         </item>
///     </list>
///     So this never calls it. It inspects <c>IsSuccess</c> / <c>GetException</c> / <c>GetValue</c> directly and
///     applies one policy at every boundary:
///     <list type="number">
///         <item>
///             <description>
///                 <b>A failure the box carries is rethrown as itself</b> — same exception instance, same type,
///                 same stack, via <see cref="ExceptionDispatchInfo" /> — whether <c>T</c> is a reference type or a
///                 value type. WithoutResult is the exception-based facade: its contract is that a
///                 <c>SekibanValidationException</c> arrives as a <c>SekibanValidationException</c> and an
///                 <see cref="OperationCanceledException" /> arrives as an
///                 <see cref="OperationCanceledException" />. Wrapping those would break every <c>catch</c> block
///                 our callers have already written. The boundary context goes on
///                 <see cref="Exception.Data" /> instead, where it adds information without changing the type.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <b>When there is no failure to rethrow, we say so.</b> A null box, a success carrying no value,
///                 or a failure carrying no exception all become one <see cref="SekibanBoundaryException" /> that
///                 names the operation. These are the cases that used to surface as an unexplained
///                 <see cref="NullReferenceException" /> — or as nothing at all.
///             </description>
///         </item>
///     </list>
/// </summary>
internal static class GuardedUnwrap
{
    /// <summary>The <see cref="Exception.Data" /> key carrying the boundary the failure crossed.</summary>
    internal const string OperationDataKey = "Sekiban.Boundary.Operation";

    /// <summary>The <see cref="Exception.Data" /> key carrying what that boundary was working on.</summary>
    internal const string TargetDataKey = "Sekiban.Boundary.Target";

    /// <summary>
    ///     Returns the value the box holds, or throws something that explains why it does not.
    /// </summary>
    internal static T Unwrap<T>(ResultBox<T>? box, BoundaryContext context) where T : notnull
    {
        if (box is null)
        {
            // The bare NRE that started all this. It said nothing; this names the boundary that produced it.
            throw new SekibanBoundaryException(
                $"{context.Describe()} returned a null ResultBox. The box was not carrying a failure — there was " +
                "no box. It means an internal path returned null instead of a result; please report it with this " +
                "message.",
                context.Operation,
                context.Target,
                null);
        }

        if (!box.IsSuccess)
        {
            var failure = box.GetException();

            if (failure is null)
            {
                throw new SekibanBoundaryException(
                    $"{context.Describe()} failed, but the result carried no exception to explain why. Please " +
                    "report it with this message.",
                    context.Operation,
                    context.Target,
                    null);
            }

            // Rethrow the original — same instance, same type, same stack — after recording where it crossed.
            // This is what UnwrapBox already did for a reference-typed T, and what it silently did NOT do for a
            // value-typed T.
            Annotate(failure, context);
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        var value = box.GetValue();

        if (value is null)
        {
            throw new SekibanBoundaryException(
                $"{context.Describe()} succeeded but produced no value. Please report it with this message.",
                context.Operation,
                context.Target,
                null);
        }

        return value;
    }

    /// <summary>Awaits the box, then <see cref="Unwrap{T}" />s it.</summary>
    internal static async Task<T> UnwrapAsync<T>(Task<ResultBox<T>> boxTask, BoundaryContext context)
        where T : notnull
    {
        // A null Task is the same class of bug as a null box, and would otherwise be an NRE on the await.
        if (boxTask is null)
        {
            throw new SekibanBoundaryException(
                $"{context.Describe()} returned a null Task. Please report it with this message.",
                context.Operation,
                context.Target,
                null);
        }

        return Unwrap(await boxTask.ConfigureAwait(false), context);
    }

    /// <summary>
    ///     Records the boundary on the exception without changing what it is. Best effort: some exceptions expose a
    ///     read-only <see cref="Exception.Data" />, and losing a diagnostic annotation must never replace the real
    ///     failure with an annotation failure.
    /// </summary>
    private static void Annotate(Exception failure, BoundaryContext context)
    {
        try
        {
            if (failure.Data.IsReadOnly || failure.Data.Contains(OperationDataKey))
            {
                // Do not overwrite: the innermost boundary the failure crossed is the informative one.
                return;
            }

            failure.Data[OperationDataKey] = context.Operation;

            if (!string.IsNullOrWhiteSpace(context.Target))
            {
                failure.Data[TargetDataKey] = context.Target;
            }
        }
        catch (Exception)
        {
            // Ignored on purpose — see the summary above.
        }
    }
}
