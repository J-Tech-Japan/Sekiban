namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Stable prefixes at the start of grain-generated query errors for host retry/HTTP mappings.
///     Transport or ResultBox wrappers may prepend text; hosts must match with
///     <c>message.Contains(prefix, StringComparison.Ordinal)</c>.
/// </summary>
public static class MultiProjectionQueryFailClosedMessages
{
    /// <summary>Start of the grain-generated error while activation catch-up is pending.</summary>
    public const string CatchUpInProgressPrefix = "Projection catch-up is in progress:";
    /// <summary>Start of the grain-generated error while projection rebuild is pending.</summary>
    public const string RebuildPendingPrefix = "Projection rebuild is pending:";
}
