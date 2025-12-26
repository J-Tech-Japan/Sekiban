using ResultBoxes;
namespace Sekiban.Dcb.Orleans.Surrogates;

[RegisterConverter]
public sealed class ResultBoxSurrogateConverter<T> : IConverter<ResultBox<T>, ResultBoxSurrogate<T>> where T : notnull
{
    public ResultBox<T> ConvertFromSurrogate(in ResultBoxSurrogate<T> surrogate)
    {
        if (surrogate.IsSuccess && surrogate.Value != null)
        {
            return ResultBox.FromValue(surrogate.Value);
        }

        if (!string.IsNullOrEmpty(surrogate.ExceptionType) && !string.IsNullOrEmpty(surrogate.ErrorMessage))
        {
            // Create a generic exception with the error details
            var exception = new Exception($"[{surrogate.ExceptionType}] {surrogate.ErrorMessage}");
            return ResultBox.FromException<T>(exception);
        }

        return ResultBox.FromException<T>(new Exception(surrogate.ErrorMessage ?? "Unknown error"));
    }

    public ResultBoxSurrogate<T> ConvertToSurrogate(in ResultBox<T> value)
    {
        var surrogate = new ResultBoxSurrogate<T>
        {
            IsSuccess = value.IsSuccess
        };

        if (value.IsSuccess)
        {
            surrogate.Value = value.GetValue();
        }
        else
        {
            var exception = value.GetException();
            surrogate.ErrorMessage = exception.Message;
            surrogate.ExceptionType = exception.GetType().Name;
        }

        return surrogate;
    }
}
