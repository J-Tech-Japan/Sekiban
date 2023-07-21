namespace Sekiban.Core.PubSub;

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
