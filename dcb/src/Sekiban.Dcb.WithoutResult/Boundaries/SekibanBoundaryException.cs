namespace Sekiban.Dcb.Boundaries;

/// <summary>
///     Thrown when a WithoutResult boundary cannot produce the value it promised, and has no failure of its own to
///     rethrow.
///     The WithoutResult packages are a facade: internally everything is a <c>ResultBox</c>, and at the edge the box
///     is opened and the value handed back. Usually a box that holds no value holds an exception instead, and that
///     exception is rethrown as itself — a <c>SekibanValidationException</c> stays a
///     <c>SekibanValidationException</c>, an <see cref="OperationCanceledException" /> stays an
///     <see cref="OperationCanceledException" />, on its original stack — so the <c>catch</c> blocks callers have
///     already written keep working. This type is NOT a wrapper around those.
///     This is for the cases where there is nothing to rethrow, and the caller would otherwise get silence or a bare
///     <see cref="NullReferenceException" />: a null box, a success carrying no value, or a failure carrying no
///     exception. Each of them means an internal path returned something it should not have. The message names the
///     boundary — the operation the caller invoked, and what it was working on — so that a bug report has somewhere
///     to start.
/// </summary>
public sealed class SekibanBoundaryException : Exception
{
    /// <summary>Creates a boundary exception.</summary>
    public SekibanBoundaryException()
    {
    }

    /// <summary>Creates a boundary exception with a message.</summary>
    public SekibanBoundaryException(string message) : base(message)
    {
    }

    /// <summary>Creates a boundary exception with a message and an underlying cause.</summary>
    public SekibanBoundaryException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>Creates a boundary exception naming the operation it was raised at.</summary>
    public SekibanBoundaryException(string message, string operation, string? target, Exception? innerException)
        : base(message, innerException!)
    {
        Operation = operation;
        Target = target;
    }

    /// <summary>
    ///     The boundary that failed, e.g. <c>ISekibanExecutor.QueryAsync</c> — the operation the caller invoked, not
    ///     some internal step of it.
    /// </summary>
    public string? Operation { get; }

    /// <summary>
    ///     What it was operating on, when there is something worth naming: the command type, the query type, the
    ///     projector, the tag. Null when the boundary has nothing more specific to say.
    /// </summary>
    public string? Target { get; }
}
