namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>Stable query error prefixes for host retry/HTTP mappings.</summary>
public static class MultiProjectionQueryFailClosedMessages
{
    public const string CatchUpInProgressPrefix = "Projection catch-up is in progress:";
    public const string RebuildPendingPrefix = "Projection rebuild is pending:";
}
