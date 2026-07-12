namespace Sekiban.Dcb.CosmosDb;

/// <summary>
///     What the Cosmos event store does when the tag-write phase of a two-phase write fails.
/// </summary>
public enum CosmosWriteFailurePolicy
{
    /// <summary>
    ///     The behavior shipped by earlier releases, kept as the default so that upgrading the package alone
    ///     changes nothing: the tag write is not retried, and if
    ///     <see cref="CosmosDbEventStoreOptions.TryRollbackOnFailure" /> is set the already-written event
    ///     documents are best-effort deleted.
    ///     Rollback deletes durable events that all-events consumers (multi-projections) may already have
    ///     observed, and it only runs on an in-process exception — never after a crash. Prefer
    ///     <see cref="RollForward" />.
    /// </summary>
    Compatible = 0,

    /// <summary>
    ///     Roll forward instead of deleting. The tag write is retried — safely, because tag rows derive
    ///     deterministically from the events (see <see cref="Tags.CosmosTagIdentity" />), so a retry converges
    ///     on the rows a partial write left missing. Written events are never deleted. If the retries are
    ///     exhausted, a <see cref="Tags.CosmosTagWriteExhaustedException" /> names the events whose tag rows
    ///     may be missing, and those events stay durable for a later repair.
    ///     This becomes the default at a major version boundary; until then it is opt-in.
    /// </summary>
    RollForward = 1
}
