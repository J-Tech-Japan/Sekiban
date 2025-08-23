using ResultBoxes;

namespace Sekiban.Dcb.Queries;

/// <summary>
/// General query result wrapper for Orleans serialization
/// </summary>
public record QueryResultGeneral(object Value, string ResultType, IQueryCommon Query) : IQueryResult
{
    public object GetValue() => Value;
    
    public ResultBox<T> ToTypedResult<T>() where T : notnull
    {
        if (Value is T typedValue)
        {
            return ResultBox.FromValue(typedValue);
        }
        
        return ResultBox.Error<T>(new InvalidCastException($"Cannot cast {Value?.GetType()?.Name ?? "null"} to {typeof(T).Name}"));
    }
}