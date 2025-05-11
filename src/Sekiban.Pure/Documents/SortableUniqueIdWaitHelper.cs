using Sekiban.Pure.Documents;

namespace Sekiban.Pure.Documents;

public static class SortableUniqueIdWaitHelper
{
    public const int DefaultWaitTimeoutMs = 30000;
    public const int DefaultPollingIntervalMs = 200;
    
    public static bool HasProcessedTargetId(string? currentId, string targetId)
    {
        if (string.IsNullOrEmpty(currentId))
        {
            return false;
        }
        
        var current = new SortableUniqueIdValue(currentId);
        var target = new SortableUniqueIdValue(targetId);
        
        return current.IsLaterThanOrEqual(target);
    }
    
    public static int CalculateAdaptiveTimeout(string sortableUniqueId)
    {
        var id = new SortableUniqueIdValue(sortableUniqueId);
        var eventTime = id.GetTicks();
        var age = DateTime.UtcNow - eventTime;
        
        if (age.TotalSeconds > 5)
        {
            return Math.Min(5000, DefaultWaitTimeoutMs);
        }
        
        return DefaultWaitTimeoutMs;
    }
}
