using DcbLib.Actors;

namespace Unit;

/// <summary>
/// Helper methods for testing async actors
/// </summary>
public static class TestHelpers
{
    /// <summary>
    /// Synchronously gets the result of an async method for testing
    /// </summary>
    public static T GetResult<T>(Task<T> task)
    {
        return task.GetAwaiter().GetResult();
    }
    
    /// <summary>
    /// Synchronously executes an async method for testing
    /// </summary>
    public static void Execute(Task task)
    {
        task.GetAwaiter().GetResult();
    }
}