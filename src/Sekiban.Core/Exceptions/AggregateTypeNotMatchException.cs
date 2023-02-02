namespace Sekiban.Core.Exceptions;

public class AggregateTypeNotMatchException : Exception, ISekibanException
{
    public AggregateTypeNotMatchException(Type expectedType, Type actualType) : base(
        $"Aggregate Type Not Match. Expected: {expectedType}, Actual: {actualType}")
    {
    }
}
