namespace Sekiban.Core.PubSub;

/// <summary>
///     Runs blocking or non-blocking action for subscription
/// </summary>
public record EventNonBlockingStatus
{
    public bool IsBlocking { get; set; }

    public void RunBlockingAction(Action action)
    {
        var originalValue = IsBlocking;
        try
        {

            IsBlocking = true;
            action();
        }
        finally
        {
            IsBlocking = originalValue;
        }
    }
    public T RunBlockingFunc<T>(Func<T> func)
    {
        var originalValue = IsBlocking;
        try
        {

            IsBlocking = true;
            return func();
        }
        finally
        {
            IsBlocking = originalValue;
        }
    }
}
