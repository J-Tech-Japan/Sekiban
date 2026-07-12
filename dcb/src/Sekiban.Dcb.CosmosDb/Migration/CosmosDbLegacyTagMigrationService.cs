using Microsoft.Extensions.Logging;
using Sekiban.Dcb.CosmosDb.Models;
using Sekiban.Dcb.CosmosDb.Repair;
using Sekiban.Dcb.CosmosDb.Tags;
namespace Sekiban.Dcb.CosmosDb.Migration;

/// <summary>
///     Collapses the legacy tag rows of an (event, tag) down to the one canonical row — by deleting the
///     others.
///     This is the only thing in the Cosmos provider that deletes a tag row, and it exists behind every lock
///     the other slices were built to avoid needing:
///     <list type="bullet">
///         <item>
///             <description>
///                 It is a separate service, on a separate store seam that is the only one able to express a
///                 delete at all. The repair service and the automatic sweep are wired to a seam that has no
///                 delete, so they cannot reach this — not by configuration, not by accident.
///             </description>
///         </item>
///         <item>
///             <description>
///                 It runs in two passes. <see cref="PlanAsync" /> mutates nothing and produces an artifact
///                 saying exactly which rows would die. <see cref="ApplyAsync" /> takes that artifact and
///                 nothing else — so an operator cannot delete rows they were not first shown.
///             </description>
///         </item>
///         <item>
///             <description>
///                 The apply refuses without an explicit confirm flag and a backup writer, backs up every
///                 row before removing any, deletes only on a matching ETag, and reports a lost race rather
///                 than forcing it.
///             </description>
///         </item>
///     </list>
///     Migration is optional: legacy rows index their events perfectly well, and the repair service
///     recognizes them. This exists to tidy up, never because correctness depends on it.
/// </summary>
public sealed class CosmosDbLegacyTagMigrationService
{
    private readonly string _eventsContainer;
    private readonly ICosmosRepairEventSource _eventSource;
    private readonly ILogger<CosmosDbLegacyTagMigrationService>? _logger;
    private readonly string _serviceId;
    private readonly ICosmosTagMigrationStore _store;
    private readonly string _tagsContainer;

    internal CosmosDbLegacyTagMigrationService(
        string serviceId,
        string eventsContainer,
        string tagsContainer,
        ICosmosRepairEventSource eventSource,
        ICosmosTagMigrationStore store,
        ILogger<CosmosDbLegacyTagMigrationService>? logger = null)
    {
        _serviceId = serviceId ?? throw new ArgumentNullException(nameof(serviceId));
        _eventsContainer = eventsContainer ?? throw new ArgumentNullException(nameof(eventsContainer));
        _tagsContainer = tagsContainer ?? throw new ArgumentNullException(nameof(tagsContainer));
        _eventSource = eventSource ?? throw new ArgumentNullException(nameof(eventSource));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger;
    }

    /// <summary>The one lineage this instance can touch. Fixed at construction.</summary>
    public string ServiceId => _serviceId;

    /// <summary>
    ///     Works out what a destructive run would do, and mutates nothing.
    ///     The artifact it returns is what an operator reads, keeps, and hands to <see cref="ApplyAsync" />.
    ///     Planning the same unchanged world twice produces the same artifact, fingerprint included.
    /// </summary>
    public async Task<CosmosTagMigrationPlan> PlanAsync(
        CosmosTagMigrationPlanOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var checkpoint = CosmosTagRepairCheckpoint.TryDecode(options.Checkpoint);
        var from = checkpoint?.LastSortableUniqueId ?? options.FromSortableUniqueIdExclusive;
        var pageSize = Math.Max(1, options.PageSize);
        var maxEvents = Math.Max(1, options.MaxEventsToScan);

        var actions = new List<CosmosTagMigrationAction>();
        var skipped = new List<CosmosTagMigrationSkip>();
        var gate = new Lock();

        string? continuationToken = null;
        string? lastSortableUniqueId = null;
        var eventsScanned = 0;
        var keysScanned = 0;

        while (eventsScanned < maxEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = await ThrottleAware.ExecuteAsync(
                () => _eventSource.ReadEventPageAsync(
                    from,
                    options.ToSortableUniqueIdInclusive,
                    Math.Min(pageSize, maxEvents - eventsScanned),
                    continuationToken,
                    cancellationToken),
                options.MaxThrottleRetries,
                cancellationToken).ConfigureAwait(false);

            foreach (var cosmosEvent in page.Events)
            {
                if (!Guid.TryParse(cosmosEvent.Id, out var eventId) || cosmosEvent.Tags == null)
                {
                    eventsScanned++;
                    lastSortableUniqueId = cosmosEvent.SortableUniqueId;
                    continue;
                }

                var tags = cosmosEvent.Tags.Distinct(StringComparer.Ordinal).ToList();
                using var semaphore = new SemaphoreSlim(Math.Max(1, options.MaxParallelism));

                await Task.WhenAll(tags.Select(async tag =>
                {
                    await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        var planned = await PlanKeyAsync(cosmosEvent, eventId, tag, options, cancellationToken)
                            .ConfigureAwait(false);

                        lock (gate)
                        {
                            keysScanned++;

                            switch (planned)
                            {
                                case { Action: not null }:
                                    actions.Add(planned.Action);
                                    break;
                                case { Skip: not null }:
                                    skipped.Add(planned.Skip);
                                    break;
                            }
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                })).ConfigureAwait(false);

                eventsScanned++;
                lastSortableUniqueId = cosmosEvent.SortableUniqueId;
            }

            continuationToken = page.ContinuationToken;
            if (continuationToken == null)
            {
                return BuildPlan(options, actions, skipped, eventsScanned, keysScanned, hasMore: false, resumeFrom: null);
            }
        }

        return BuildPlan(
            options,
            actions,
            skipped,
            eventsScanned,
            keysScanned,
            hasMore: true,
            resumeFrom: lastSortableUniqueId ?? from);
    }

    /// <summary>
    ///     Executes a plan. This is the destructive one.
    ///     Refuses unless the operator confirmed and supplied a backup writer; refuses a plan that is missing,
    ///     built for another lineage, or altered since it was produced. Backs up every row it is about to
    ///     delete before deleting any of them. Deletes only on a matching ETag, and reports a lost race
    ///     instead of forcing it. Records an audit entry for every key it touches — and for every key it
    ///     declines to touch.
    /// </summary>
    public async Task<CosmosTagMigrationReport> ApplyAsync(
        CosmosTagMigrationPlan plan,
        CosmosTagMigrationApplyOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        // The authorization gate, before anything is read, let alone written.
        if (plan == null)
        {
            throw new CosmosTagMigrationPlanRejectedException(
                "A destructive migration needs the plan a dry run produced. There is no way to delete rows " +
                "without first producing — and reading — the artifact that says which rows they are.");
        }

        if (!options.Confirm)
        {
            throw new CosmosTagMigrationNotAuthorizedException(
                "The destructive tag migration was not confirmed. Set CosmosTagMigrationApplyOptions.Confirm " +
                $"to delete the {plan.RowsToRemoveCount} row(s) this plan describes. Nothing was touched.");
        }

        if (options.BackupWriter == null)
        {
            throw new CosmosTagMigrationNotAuthorizedException(
                "The destructive tag migration needs a backup writer: Cosmos has no undo, so the rows are " +
                "exported before they are removed. Nothing was touched.");
        }

        RejectPlanIfNotOurs(plan);

        var audit = new List<CosmosTagMigrationAuditEntry>();
        var gate = new Lock();

        // Every row this run intends to delete, read fresh, and written to the backup BEFORE the first
        // delete. If the backup throws, nothing has been removed.
        var (backupRows, freshness) = await ReadRowsToRemoveAsync(plan, options, cancellationToken)
            .ConfigureAwait(false);

        await options.BackupWriter.WriteAsync(plan, backupRows, cancellationToken).ConfigureAwait(false);

        if (_logger != null)
            LogBackupWritten(_logger, _serviceId, backupRows.Count, null);

        using var semaphore = new SemaphoreSlim(Math.Max(1, options.MaxParallelism));

        await Task.WhenAll(plan.Actions.Select(async action =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var entry = freshness.TryGetValue(ActionKey(action), out var stale) && stale
                    ? new CosmosTagMigrationAuditEntry
                    {
                        EventId = action.EventId,
                        Tag = action.Tag,
                        PartitionKey = action.PartitionKey,
                        SurvivorId = action.SurvivorId,
                        Outcome = CosmosTagMigrationOutcome.Stale,
                        Detail = "the rows changed since the plan was produced; re-plan and review again"
                    }
                    : await ApplyActionAsync(action, options, cancellationToken).ConfigureAwait(false);

                lock (gate)
                {
                    audit.Add(entry);
                }
            }
            finally
            {
                semaphore.Release();
            }
        })).ConfigureAwait(false);

        foreach (var skip in plan.Skipped)
        {
            audit.Add(new CosmosTagMigrationAuditEntry
            {
                EventId = skip.EventId,
                Tag = skip.Tag,
                Outcome = CosmosTagMigrationOutcome.Skipped,
                Detail = $"{skip.Reason}: {skip.Detail}"
            });
        }

        var report = new CosmosTagMigrationReport
        {
            KeysPlanned = plan.Actions.Count,
            Reduced = audit.Count(entry => entry.Outcome == CosmosTagMigrationOutcome.Reduced),
            RowsRemoved = audit.Sum(entry => entry.RemovedIds.Count),
            SurvivorsCreated = audit.Count(entry => entry.SurvivorCreated),
            LostRaces = audit.Count(entry => entry.Outcome == CosmosTagMigrationOutcome.LostRaceContentChanged),
            StaleSurvivors = audit.Count(entry => entry.Outcome == CosmosTagMigrationOutcome.StaleSurvivor),
            Stale = audit.Count(entry => entry.Outcome == CosmosTagMigrationOutcome.Stale),
            Skipped = audit.Count(entry => entry.Outcome == CosmosTagMigrationOutcome.Skipped),
            Audit = audit
        };

        if (_logger != null)
        {
            foreach (var entry in report.Audit.Where(entry => entry.Outcome == CosmosTagMigrationOutcome.Reduced))
            {
                LogKeyReduced(
                    _logger,
                    _serviceId,
                    entry.Tag,
                    entry.EventId,
                    entry.SurvivorId,
                    string.Join(", ", entry.RemovedIds),
                    null);
            }

            LogMigrationCompleted(_logger, _serviceId, report.Reduced, report.RowsRemoved, report.LostRaces, report.Stale, null);
        }

        return report;
    }

    private void RejectPlanIfNotOurs(CosmosTagMigrationPlan plan)
    {
        if (!string.Equals(plan.ServiceId, _serviceId, StringComparison.Ordinal) ||
            !string.Equals(plan.EventsContainer, _eventsContainer, StringComparison.Ordinal) ||
            !string.Equals(plan.TagsContainer, _tagsContainer, StringComparison.Ordinal))
        {
            throw new CosmosTagMigrationPlanRejectedException(
                $"This plan was produced for service '{plan.ServiceId}' " +
                $"({plan.EventsContainer}/{plan.TagsContainer}), and this migration is bound to " +
                $"'{_serviceId}' ({_eventsContainer}/{_tagsContainer}). Applying a plan across lineages is " +
                "refused. Nothing was touched.");
        }

        // An artifact that no longer hashes to its own fingerprint has been edited or corrupted. It no
        // longer describes what an operator reviewed, so it has no authority to delete anything.
        var recomputed = plan.ComputeFingerprint();
        if (!string.Equals(plan.Fingerprint, recomputed, StringComparison.Ordinal))
        {
            throw new CosmosTagMigrationPlanRejectedException(
                "The plan's fingerprint does not match its contents: it has been altered since it was " +
                "produced, so it no longer describes what was reviewed. Re-run the dry run. Nothing was touched.");
        }
    }

    /// <summary>
    ///     Reads every row the plan wants gone, as it is right now, and notes which keys have moved since the
    ///     plan pinned them. A key whose rows have changed is not applied — the plan's authority over it has
    ///     lapsed.
    /// </summary>
    private async Task<(IReadOnlyList<CosmosTag> Rows, Dictionary<string, bool> Stale)> ReadRowsToRemoveAsync(
        CosmosTagMigrationPlan plan,
        CosmosTagMigrationApplyOptions options,
        CancellationToken cancellationToken)
    {
        var rows = new List<CosmosTag>();
        var stale = new Dictionary<string, bool>(StringComparer.Ordinal);

        foreach (var action in plan.Actions)
        {
            var isStale = false;

            foreach (var planned in action.RowsToRemove)
            {
                var live = await ThrottleAware.ExecuteAsync(
                    () => _store.TryReadRowAsync(action.PartitionKey, planned.Id, cancellationToken),
                    options.MaxThrottleRetries,
                    cancellationToken).ConfigureAwait(false);

                if (live == null)
                {
                    // Already gone. Not stale — just nothing to back up or delete.
                    continue;
                }

                // Content is what decides. A row whose ETag moved but whose content is untouched is still
                // the row the operator reviewed; a row whose content moved is not, whatever its ETag says.
                if (!planned.Snapshot.Matches(live))
                {
                    isStale = true;
                    continue;
                }

                rows.Add(live);
            }

            stale[ActionKey(action)] = isStale;
        }

        return (rows, stale);
    }

    /// <summary>
    ///     Reduces one key — atomically.
    ///     The survivor's guarantee and the victims' deletion are ONE transaction, not a proof followed by a
    ///     removal. Proving the survivor with a read and then deleting is a check followed by a use, and the
    ///     world can move in between: the survivor could be removed after the proof and the deletes would
    ///     still commit, leaving the key indexed by nothing. Re-reading more often narrows that window; it
    ///     does not close it. So there is no window. Either the key ends up canonical, or nothing about it
    ///     changed.
    /// </summary>
    private async Task<CosmosTagMigrationAuditEntry> ApplyActionAsync(
        CosmosTagMigrationAction action,
        CosmosTagMigrationApplyOptions options,
        CancellationToken cancellationToken)
    {
        // The canonical row, derived from the event: what the write path would have written, and therefore
        // what survives. Never a promoted legacy row.
        var survivor = CosmosTagIdentity.DeriveRow(
            _serviceId,
            action.Tag,
            action.EventId,
            action.SurvivorExpected.SortableUniqueId,
            action.SurvivorExpected.EventType);

        var outcome = await ThrottleAware.ExecuteAsync(
            () => _store.ExecuteReduceAsync(
                action.PartitionKey,
                survivor,
                action.SurvivorExists,
                action.SurvivorETag,
                action.RowsToRemove,
                cancellationToken),
            options.MaxThrottleRetries,
            cancellationToken).ConfigureAwait(false);

        return outcome switch
        {
            CosmosReduceOutcome.Committed => new CosmosTagMigrationAuditEntry
            {
                EventId = action.EventId,
                Tag = action.Tag,
                PartitionKey = action.PartitionKey,
                SurvivorId = action.SurvivorId,
                SurvivorCreated = !action.SurvivorExists,
                RemovedIds = action.RowsToRemove.Select(row => row.Id).ToList(),
                Outcome = CosmosTagMigrationOutcome.Reduced
            },

            CosmosReduceOutcome.SurvivorRejected => new CosmosTagMigrationAuditEntry
            {
                EventId = action.EventId,
                Tag = action.Tag,
                PartitionKey = action.PartitionKey,
                SurvivorId = action.SurvivorId,
                // The transaction was refused, so nothing was created and nothing was removed. The audit says
                // exactly that — it never claims a row died in a transaction that did not commit.
                Outcome = CosmosTagMigrationOutcome.StaleSurvivor,
                Detail = "the canonical row is not what the plan required — it has appeared, vanished, or " +
                    "been rewritten since. The transaction was refused, so NOT ONE row was deleted. Re-plan."
            },

            CosmosReduceOutcome.TooManyOperations => new CosmosTagMigrationAuditEntry
            {
                EventId = action.EventId,
                Tag = action.Tag,
                PartitionKey = action.PartitionKey,
                SurvivorId = action.SurvivorId,
                Outcome = CosmosTagMigrationOutcome.Skipped,
                Detail = $"this key needs {action.RowsToRemove.Count + 1} operations, more than one Cosmos " +
                    "transaction can carry. Splitting it would reintroduce the gap between the survivor's " +
                    "guarantee and the deletes, so it is refused. Nothing was touched."
            },

            _ => new CosmosTagMigrationAuditEntry
            {
                EventId = action.EventId,
                Tag = action.Tag,
                PartitionKey = action.PartitionKey,
                SurvivorId = action.SurvivorId,
                Outcome = CosmosTagMigrationOutcome.LostRaceContentChanged,
                Detail = "a row to remove no longer matches the version the plan pinned. The transaction was " +
                    "refused, so NOT ONE row was deleted — not even the ones that had not moved. Re-plan."
            }
        };
    }

    private async Task<(CosmosTagMigrationAction? Action, CosmosTagMigrationSkip? Skip)> PlanKeyAsync(
        CosmosEvent cosmosEvent,
        Guid eventId,
        string tag,
        CosmosTagMigrationPlanOptions options,
        CancellationToken cancellationToken)
    {
        var derived = CosmosTagIdentity.DeriveRow(
            _serviceId,
            tag,
            eventId,
            cosmosEvent.SortableUniqueId,
            cosmosEvent.EventType);

        var (rows, overflowed) = await ThrottleAware.ExecuteAsync(
            () => _store.ReadRowsForEventAsync(
                derived.Pk,
                eventId,
                Math.Max(1, options.MaxRowsPerKey),
                cancellationToken),
            options.MaxThrottleRetries,
            cancellationToken).ConfigureAwait(false);

        if (overflowed)
        {
            return (null, new CosmosTagMigrationSkip(
                eventId,
                tag,
                CosmosTagMigrationSkipReason.Overflow,
                $"more than {options.MaxRowsPerKey} rows index this key; not reduced"));
        }

        var planned = CosmosTagSurvivorPolicy.Plan(rows, derived);
        if (planned == null)
        {
            return (null, null);
        }

        var (_, survivorExists, toRemove) = planned.Value;

        // A row that disagrees with its event is not a duplicate — it is corruption, and deleting it is not
        // this service's call to make.
        if (!CosmosTagSurvivorPolicy.AllRowsAreSafeToRemove(toRemove, derived, out var detail))
        {
            return (null, new CosmosTagMigrationSkip(
                eventId,
                tag,
                CosmosTagMigrationSkipReason.Corrupt,
                detail));
        }

        // The reduce is one transaction — survivor plus every victim — so a key that needs more operations
        // than a transaction can carry is refused HERE, before anything is planned, let alone mutated.
        // Splitting it into several transactions would give back exactly the window the atomic boundary
        // removes.
        if (toRemove.Count > CosmosContainerMigrationStore.MaxVictimsPerBatch)
        {
            return (null, new CosmosTagMigrationSkip(
                eventId,
                tag,
                CosmosTagMigrationSkipReason.BatchLimit,
                $"{toRemove.Count} rows to remove plus the survivor exceeds the " +
                $"{CosmosContainerMigrationStore.MaxBatchOperations}-operation limit of one Cosmos " +
                "transaction; the key is left alone rather than reduced non-atomically"));
        }

        var liveSurvivor = rows.FirstOrDefault(row =>
            string.Equals(row.Id, derived.Id, StringComparison.Ordinal));

        return (new CosmosTagMigrationAction
        {
            EventId = eventId,
            Tag = tag,
            PartitionKey = derived.Pk,
            SurvivorId = derived.Id,
            SurvivorExists = survivorExists,

            // What the canonical row MUST be, derived from the event. The run proves the live survivor
            // matches this before it deletes anything.
            SurvivorExpected = CosmosTagRowSnapshot.From(derived),
            SurvivorETag = liveSurvivor?.ETag,

            // Each removable row pinned to its version AND its full content.
            RowsToRemove = toRemove
                .Select(row => new CosmosTagRowRef
                {
                    Id = row.Id,
                    ETag = row.ETag,
                    Snapshot = CosmosTagRowSnapshot.From(row)
                })
                .ToList()
        }, null);
    }

    private CosmosTagMigrationPlan BuildPlan(
        CosmosTagMigrationPlanOptions options,
        List<CosmosTagMigrationAction> actions,
        List<CosmosTagMigrationSkip> skipped,
        int eventsScanned,
        int keysScanned,
        bool hasMore,
        string? resumeFrom)
    {
        var plan = new CosmosTagMigrationPlan
        {
            ServiceId = _serviceId,
            EventsContainer = _eventsContainer,
            TagsContainer = _tagsContainer,
            FromSortableUniqueIdExclusive = options.FromSortableUniqueIdExclusive,
            ToSortableUniqueIdInclusive = options.ToSortableUniqueIdInclusive,
            EventsScanned = eventsScanned,
            KeysScanned = keysScanned,
            Actions = actions
                .OrderBy(action => action.PartitionKey, StringComparer.Ordinal)
                .ThenBy(action => action.EventId.ToString(), StringComparer.Ordinal)
                .ToList(),
            Skipped = skipped
                .OrderBy(skip => skip.Tag, StringComparer.Ordinal)
                .ThenBy(skip => skip.EventId.ToString(), StringComparer.Ordinal)
                .ToList(),
            HasMore = hasMore,
            Checkpoint = hasMore && resumeFrom != null
                ? new CosmosTagRepairCheckpoint(resumeFrom).Encode()
                : null
        };

        return plan with { Fingerprint = plan.ComputeFingerprint() };
    }

    private static string ActionKey(CosmosTagMigrationAction action) =>
        $"{action.PartitionKey}|{action.EventId}";

    private static readonly Action<ILogger, string, int, Exception?> LogBackupWritten =
        LoggerMessage.Define<string, int>(
            LogLevel.Information,
            new EventId(1, nameof(LogBackupWritten)),
            "Tag migration for service '{ServiceId}': backed up {RowCount} row(s) before removing any");

    private static readonly Action<ILogger, string, string, Guid, string, string, Exception?> LogKeyReduced =
        LoggerMessage.Define<string, string, Guid, string, string>(
            LogLevel.Information,
            new EventId(2, nameof(LogKeyReduced)),
            "Tag migration for service '{ServiceId}': tag '{Tag}', event {EventId} reduced to survivor " +
            "'{SurvivorId}'; removed [{RemovedIds}]");

    private static readonly Action<ILogger, string, int, int, int, int, Exception?> LogMigrationCompleted =
        LoggerMessage.Define<string, int, int, int, int>(
            LogLevel.Information,
            new EventId(3, nameof(LogMigrationCompleted)),
            "Tag migration for service '{ServiceId}' completed: {Reduced} key(s) reduced, {RowsRemoved} row(s) " +
            "removed, {LostRaces} lost race(s), {Stale} stale key(s)");
}
