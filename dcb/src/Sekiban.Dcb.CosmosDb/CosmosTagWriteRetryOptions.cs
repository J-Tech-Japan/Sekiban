namespace Sekiban.Dcb.CosmosDb;

/// <summary>
///     Retry policy for the tag-write phase. Only consulted when
///     <see cref="CosmosDbEventStoreOptions.WriteFailurePolicy" /> is
///     <see cref="CosmosWriteFailurePolicy.RollForward" /> — under the compatible default the tag write is
///     not retried, so these values cannot change the behavior of an existing deployment.
/// </summary>
public class CosmosTagWriteRetryOptions
{
    /// <summary>
    ///     Total attempts, including the first. 1 disables retrying. Default: 5.
    /// </summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    ///     Backoff before the first retry. Doubles per attempt up to <see cref="MaxBackoff" />. Default: 200ms.
    /// </summary>
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    ///     Upper bound for a single backoff. Default: 5 seconds.
    /// </summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Overall deadline for the retry sequence, measured from the first attempt. Once it passes, no
    ///     further attempt starts even if attempts remain. Default: 30 seconds.
    /// </summary>
    public TimeSpan MaxTotalDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Fraction of each backoff randomized away, to keep concurrent writers from retrying in lockstep.
    ///     0 disables jitter; 0.2 means a backoff is drawn from 80%–100% of its computed value. Default: 0.2.
    /// </summary>
    public double JitterRatio { get; set; } = 0.2;
}
