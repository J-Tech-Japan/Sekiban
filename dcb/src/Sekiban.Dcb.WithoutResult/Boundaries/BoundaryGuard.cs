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
///     Applies one policy at every WithoutResult boundary. Core is exception based, so a boundary has nothing to
///     open: it runs the core call and lets its failure through.
///     <list type="number">
///         <item>
///             <description>
///                 <b>A failure is rethrown as itself</b> — same exception instance, same type, same stack, via
///                 <see cref="ExceptionDispatchInfo" />. WithoutResult is the exception-based facade: its contract is
///                 that a <c>SekibanValidationException</c> arrives as a <c>SekibanValidationException</c> and an
///                 <see cref="OperationCanceledException" /> arrives as an <see cref="OperationCanceledException" />.
///                 Wrapping those would break every <c>catch</c> block our callers have already written. The boundary
///                 context goes on <see cref="Exception.Data" /> instead, where it adds information without changing
///                 the type.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <b>When there is no failure to rethrow, we say so.</b> A null Task or a null result becomes one
///                 <see cref="SekibanBoundaryException" /> that names the operation, instead of an unexplained
///                 <see cref="NullReferenceException" />.
///             </description>
///         </item>
///     </list>
/// </summary>
internal static class BoundaryGuard
{
    /// <summary>The <see cref="Exception.Data" /> key carrying the boundary the failure crossed.</summary>
    internal const string OperationDataKey = "Sekiban.Boundary.Operation";

    /// <summary>The <see cref="Exception.Data" /> key carrying what that boundary was working on.</summary>
    internal const string TargetDataKey = "Sekiban.Boundary.Target";

    /// <summary>Serialises annotation, so an operation/target pair can never come from two different boundaries.</summary>
    private static readonly object AnnotationGate = new();

    /// <summary>
    ///     Runs an exception-based core call at a boundary. A failure is rethrown as itself after the boundary is
    ///     recorded on it; a null result becomes a <see cref="SekibanBoundaryException" /> naming the boundary.
    /// </summary>
    internal static T Run<T>(Func<T> call, BoundaryContext context) where T : notnull
    {
        T value;
        try
        {
            value = call();
        }
        catch (Exception failure)
        {
            Annotate(failure, context);
            ExceptionDispatchInfo.Capture(failure).Throw();
            throw; // unreachable: Throw() above does not return. Here to satisfy definite assignment.
        }

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
    ///     Awaits an exception-based core call at a boundary. A failure is rethrown as itself after the boundary is
    ///     recorded on it; a null Task or a null result becomes a
    ///     <see cref="SekibanBoundaryException" /> naming the boundary.
    /// </summary>
    internal static async Task<T> AwaitAsync<T>(Task<T> task, BoundaryContext context) where T : notnull
    {
        if (task is null)
        {
            throw new SekibanBoundaryException(
                $"{context.Describe()} returned a null Task. Please report it with this message.",
                context.Operation,
                context.Target,
                null);
        }

        T value;
        try
        {
            value = await task.ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            Annotate(failure, context);
            ExceptionDispatchInfo.Capture(failure).Throw();
            throw; // unreachable: Throw() above does not return. Here to satisfy definite assignment.
        }

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
    ///     Records the boundary on a failure the boundary itself is about to throw, and returns it for the throw.
    /// </summary>
    internal static TException Annotated<TException>(TException failure, BoundaryContext context)
        where TException : Exception
    {
        Annotate(failure, context);
        return failure;
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
