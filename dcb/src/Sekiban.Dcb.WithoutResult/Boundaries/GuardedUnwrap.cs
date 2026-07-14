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

    /// <summary>Serialises annotation, so an operation/target pair can never come from two different boundaries.</summary>
    private static readonly object AnnotationGate = new();

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

    /// <summary>
    ///     Awaits the box, then <see cref="Unwrap{T}" />s it.
    ///     A failure can reach a boundary two ways: carried inside the box, or thrown by the Task that was supposed
    ///     to produce the box. Both are the same failure to the caller, so both get the same treatment — the
    ///     original exception, rethrown as itself, carrying the boundary it crossed.
    /// </summary>
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

        ResultBox<T>? box;

        try
        {
            box = await boxTask.ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            // A faulted or cancelled Task never got as far as producing a box. Without this, the exception would
            // reach the caller with no idea which boundary it crossed — the same blindness the box path fixes.
            Annotate(failure, context);
            ExceptionDispatchInfo.Capture(failure).Throw();
            throw; // unreachable: Throw() above does not return. Here to satisfy definite assignment.
        }

        return Unwrap(box, context);
    }

    /// <summary>
    ///     Records the boundary on the exception without changing what it is.
    ///     Two properties matter, and neither is free:
    ///     <list type="bullet">
    ///         <item>
    ///             <description>
    ///                 <b>Atomic.</b> The operation and the target are one context, and must never be interleaved
    ///                 into a pair that describes two different boundaries. <see cref="Exception.Data" /> is a plain
    ///                 <see cref="System.Collections.IDictionary" /> with no thread-safety promise, and the same
    ///                 exception instance can legitimately cross concurrent boundaries — one shared failure observed
    ///                 by several in-flight tasks. So the read and both writes happen under one lock.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 <b>Best effort.</b> Some exceptions expose a read-only <c>Data</c>. Losing a diagnostic
    ///                 annotation must never replace the real failure with an annotation failure.
    ///             </description>
    ///         </item>
    ///     </list>
    ///     The lock is a single static gate rather than one per exception: annotating happens only on a failure
    ///     path, so the contention it can cause is bounded by how fast the system is failing.
    /// </summary>
    private static void Annotate(Exception failure, BoundaryContext context)
    {
        try
        {
            lock (AnnotationGate)
            {
                if (failure.Data.IsReadOnly || failure.Data.Contains(OperationDataKey))
                {
                    // First writer wins, and it is the innermost boundary: a failure is annotated as it is rethrown,
                    // so the boundary nearest the failure gets there first, and outer boundaries must not overwrite
                    // it as the same exception unwinds through them.
                    return;
                }

                failure.Data[OperationDataKey] = context.Operation;

                if (!string.IsNullOrWhiteSpace(context.Target))
                {
                    failure.Data[TargetDataKey] = context.Target;
                }
            }
        }
        catch (Exception)
        {
            // Ignored on purpose — see the summary above.
        }
    }
}
