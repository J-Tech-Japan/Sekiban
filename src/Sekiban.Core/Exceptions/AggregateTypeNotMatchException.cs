namespace Sekiban.Core.Exceptions;

/// <summary>
///     Aggregate Type from Event not match to actual aggregate payload type.
///     It could happen with inconsistent event payload type.
/// </summary>
public class AggregateTypeNotMatchException : Exception, ISekibanException
{
    /// <summary>
    ///     Exception Constructor
    /// </summary>
    /// <param name="expectedType"></param>
    /// <param name="actualType"></param>
    public AggregateTypeNotMatchException(Type expectedType, Type actualType) : base(
        $"Aggregate Type Not Match. Expected: {expectedType}, Actual: {actualType}")
    {
    }
}
