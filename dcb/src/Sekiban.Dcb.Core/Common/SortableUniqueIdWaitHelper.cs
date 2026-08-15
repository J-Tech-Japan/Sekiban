namespace Sekiban.Dcb.Common;

/// <summary>
///     Helper class for waiting on sortable unique IDs
/// </summary>
public static class SortableUniqueIdWaitHelper
{
    public const int DefaultWaitTimeoutMs = 30000;
    public const int DefaultPollingIntervalMs = 200;

    /// <summary>
    ///     Check if the current ID has processed the target ID
    /// </summary>
    public static bool HasProcessedTargetId(string? currentId, string targetId)
    {
        if (string.IsNullOrEmpty(currentId))
        {
            return false;
        }

        // Compare sortable unique IDs - if current >= target, then target has been processed
        var comparison = string.Compare(currentId, targetId, StringComparison.Ordinal);
        return comparison >= 0;
    }

    /// <summary>
    ///     Calculate adaptive timeout based on how old the sortable unique ID is
    /// </summary>
    public static int CalculateAdaptiveTimeout(string sortableUniqueId)
        => CalculateAdaptiveTimeout(sortableUniqueId, TimeProvider.System);

    /// <summary>
    ///     Calculate the legacy adaptive timeout using an injected clock. The threshold and boundary are intentionally
    ///     unchanged: IDs more than five seconds old use five seconds; IDs exactly five seconds old, future IDs, and
    ///     unparsable IDs use the thirty-second default.
    /// </summary>
    internal static int CalculateAdaptiveTimeout(string sortableUniqueId, TimeProvider timeProvider)
    {
        try
        {
            var id = new SortableUniqueId(sortableUniqueId);
            var eventTime = id.GetDateTime();
            var age = timeProvider.GetUtcNow().UtcDateTime - eventTime;

            // If the event is more than 5 seconds old, use a shorter timeout
            if (age.TotalSeconds > 5)
            {
                return Math.Min(5000, DefaultWaitTimeoutMs);
            }

            return DefaultWaitTimeoutMs;
        }
        catch
        {
            // If we can't parse the ID, use default timeout
            return DefaultWaitTimeoutMs;
        }
    }
}
