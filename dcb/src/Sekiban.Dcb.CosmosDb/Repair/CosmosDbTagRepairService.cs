using Microsoft.Extensions.Logging;
using Sekiban.Dcb.CosmosDb.Models;
using Sekiban.Dcb.CosmosDb.Tags;
namespace Sekiban.Dcb.CosmosDb.Repair;

/// <summary>
///     Administrative service that rebuilds missing tag rows from the events container.
///     A crash between the two phases of a Cosmos write can leave a durable event with no tag rows: visible
///     to all-events readers, invisible to tag-scoped reads, tag projectors, and the tag-consistent actor's
///     concurrency baseline. Every event document carries its complete <c>tags</c> array, so the tags
///     container is a derivable index — this service turns that fact into a recovery surface. It scans events
///     over a sortableUniqueId range, derives the expected row for each (event, tag) pair with the same rule
///     the write path uses (<see cref="CosmosTagIdentity" />), and creates the ones that are missing.
///     It is deliberately NOT part of <c>IEventStore</c>: it is an operator tool, to be run as a job, not
///     reachable from application request paths.
///     STRICTLY NON-DESTRUCTIVE. It only ever creates rows that do not exist. It never deletes, rewrites, or
///     canonicalizes a row — including the pre-SEK-G2 rows that sit at a random id, which it recognizes and
///     leaves exactly where they are. Reducing those is a separately authorized concern (SEK-G4b) and is
///     never invoked from here.
///     One instance is bound to one (serviceId, events container, tags container) lineage, so repairing
///     across lineages is structurally impossible rather than merely discouraged.
/// </summary>
public sealed class CosmosDbTagRepairService
{
    private readonly ICosmosRepairEventSource _eventSource;
    private readonly ILogger<CosmosDbTagRepairService>? _logger;
    private readonly string _serviceId;
    private readonly ICosmosTagRepairStore _tagStore;

    internal CosmosDbTagRepairService(
        string serviceId,
        ICosmosRepairEventSource eventSource,
        ICosmosTagRepairStore tagStore,
        ILogger<CosmosDbTagRepairService>? logger = null)
    {
        _serviceId = serviceId ?? throw new ArgumentNullException(nameof(serviceId));
        _eventSource = eventSource ?? throw new ArgumentNullException(nameof(eventSource));
        _tagStore = tagStore ?? throw new ArgumentNullException(nameof(tagStore));
        _logger = logger;
    }

    /// <summary>
    ///     The service id this instance repairs. An instance can only ever touch this lineage.
    /// </summary>
    public string ServiceId => _serviceId;

    /// <summary>
    ///     Scans the configured range and, unless <see cref="CosmosTagRepairOptions.DryRun" /> is set, writes
    ///     the tag rows that are missing.
    ///     The run stops at <see cref="CosmosTagRepairOptions.MaxEventsToScan" /> and hands back a checkpoint,
    ///     so a large repair is a sequence of bounded runs rather than one unbounded scan. Re-running is
    ///     harmless: a row that already exists is recognized, not rewritten.
    /// </summary>
    public async Task<CosmosTagRepairReport> RepairAsync(
        CosmosTagRepairOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var state = new RepairState(options);
        var checkpoint = CosmosTagRepairCheckpoint.TryDecode(options.Checkpoint);

        // The query is fixed for the whole run: `from` is where this run starts and never moves, so the
        // continuation token below stays coherent with it.
        var from = checkpoint?.LastSortableUniqueId ?? options.FromSortableUniqueIdExclusive;
        string? continuationToken = null;
        var pageSize = Math.Max(1, options.PageSize);
        var maxEvents = Math.Max(1, options.MaxEventsToScan);

        try
        {
            while (state.EventsScanned < maxEvents)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var page = await ThrottleAware.ExecuteAsync(
                    () => _eventSource.ReadEventPageAsync(
                        from,
                        options.ToSortableUniqueIdInclusive,
                        Math.Min(pageSize, maxEvents - state.EventsScanned),
                        continuationToken,
                        cancellationToken),
                    options.MaxThrottleRetries,
                    cancellationToken).ConfigureAwait(false);

                state.AddRequestCharge(page.RequestCharge);

                foreach (var cosmosEvent in page.Events)
                {
                    await ClassifyAndRepairEventAsync(cosmosEvent, options, state, cancellationToken)
                        .ConfigureAwait(false);

                    // Only counted once the event is fully classified and repaired, so a checkpoint never
                    // points past work that did not finish.
                    state.MarkEventScanned(cosmosEvent.SortableUniqueId);
                }

                continuationToken = page.ContinuationToken;

                if (continuationToken == null)
                {
                    // The range is fully scanned; there is nothing to resume from.
                    return state.ToReport(hasMore: false, checkpoint: null);
                }
            }
        }
        catch (OperationCanceledException ex)
        {
            // Cancelling does not undo the events this run already settled. Throwing that progress away
            // would leave a caller with a tight budget restarting from the same place every turn, never
            // advancing — so the partial report, checkpoint included, rides out on the exception.
            throw new CosmosTagRepairCancelledException(
                state.ToReport(hasMore: true, checkpoint: BuildResumeCheckpoint(state, from)),
                ex.CancellationToken);
        }

        // The next run starts after the last event this one settled. Resuming by sortableUniqueId rather
        // than by continuation token means an expired token cannot lose our place.
        return state.ToReport(hasMore: true, checkpoint: BuildResumeCheckpoint(state, from));
    }

    /// <summary>
    ///     Where the next run should pick up: after the last event this one fully settled. Null when nothing
    ///     was settled — there is no progress to resume from, so the next run starts where this one did.
    /// </summary>
    private static string? BuildResumeCheckpoint(RepairState state, string? from)
    {
        var last = state.LastSortableUniqueId ?? from;
        return last == null ? null : new CosmosTagRepairCheckpoint(last).Encode();
    }

    private async Task ClassifyAndRepairEventAsync(
        CosmosEvent cosmosEvent,
        CosmosTagRepairOptions options,
        RepairState state,
        CancellationToken cancellationToken)
    {
        var tags = cosmosEvent.Tags;
        if (tags == null || tags.Count == 0)
        {
            return;
        }

        if (!Guid.TryParse(cosmosEvent.Id, out var eventId))
        {
            state.Add(
                new CosmosTagRepairFinding(
                    CosmosTagRepairCategory.Corrupt,
                    Guid.Empty,
                    string.Empty,
                    cosmosEvent.SortableUniqueId,
                    $"event id '{cosmosEvent.Id}' is not a Guid"));
            return;
        }

        // An (event, tag) pair is one row, so a tag repeated within an event is one key.
        var distinctTags = tags.Distinct(StringComparer.Ordinal).ToList();

        using var semaphore = new SemaphoreSlim(Math.Max(1, options.MaxParallelism));
        var tasks = distinctTags.Select(async tag =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ClassifyAndRepairKeyAsync(cosmosEvent, eventId, tag, options, state, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task ClassifyAndRepairKeyAsync(
        CosmosEvent cosmosEvent,
        Guid eventId,
        string tag,
        CosmosTagRepairOptions options,
        RepairState state,
        CancellationToken cancellationToken)
    {
        var derived = CosmosTagIdentity.DeriveRow(
            _serviceId,
            tag,
            eventId,
            cosmosEvent.SortableUniqueId,
            cosmosEvent.EventType);

        var lookup = await ThrottleAware.ExecuteAsync(
            () => _tagStore.ReadRowsForEventAsync(
                derived.Pk,
                eventId,
                Math.Max(1, options.MaxRowsPerKey),
                cancellationToken),
            options.MaxThrottleRetries,
            cancellationToken).ConfigureAwait(false);

        state.MarkKeyScanned();
        state.AddRequestCharge(lookup.RequestCharge);

        if (lookup.Overflowed)
        {
            state.Add(
                new CosmosTagRepairFinding(
                    CosmosTagRepairCategory.Overflow,
                    eventId,
                    tag,
                    cosmosEvent.SortableUniqueId,
                    $"more than {options.MaxRowsPerKey} rows index this key; not classified"));
            return;
        }

        var classification = Classify(lookup.Rows, derived, eventId, tag, cosmosEvent, state);
        if (classification != CosmosTagRepairCategory.Missing)
        {
            return;
        }

        if (options.DryRun)
        {
            return;
        }

        await RepairMissingKeyAsync(derived, eventId, tag, cosmosEvent, options, state, cancellationToken)
            .ConfigureAwait(false);
    }

    private static CosmosTagRepairCategory Classify(
        IReadOnlyList<CosmosTag> rows,
        CosmosTag derived,
        Guid eventId,
        string tag,
        CosmosEvent cosmosEvent,
        RepairState state)
    {
        if (rows.Count == 0)
        {
            state.Add(
                new CosmosTagRepairFinding(
                    CosmosTagRepairCategory.Missing,
                    eventId,
                    tag,
                    cosmosEvent.SortableUniqueId));
            return CosmosTagRepairCategory.Missing;
        }

        // A row sitting at the derived id is the row the write path meant to write, so it is held to the
        // strict comparator — a differing createdAt there is corruption, not legacy formatting.
        var deterministic = rows.FirstOrDefault(row => string.Equals(row.Id, derived.Id, StringComparison.Ordinal));
        if (deterministic != null)
        {
            if (CosmosTagIdentity.ContentEquals(derived, deterministic))
            {
                state.MarkPresent();
                return CosmosTagRepairCategory.Present;
            }

            state.Add(
                new CosmosTagRepairFinding(
                    CosmosTagRepairCategory.Corrupt,
                    eventId,
                    tag,
                    cosmosEvent.SortableUniqueId,
                    $"row at the derived id disagrees with the event. Expected [{CosmosTagIdentity.Describe(derived)}], " +
                    $"actual [{CosmosTagIdentity.Describe(deterministic)}]"));
            return CosmosTagRepairCategory.Corrupt;
        }

        // Everything else in this partition carrying our event id is a legacy row — unless it disagrees
        // with the event, in which case it is corrupt regardless of its id.
        var legacyRows = new List<CosmosTag>();
        foreach (var row in rows)
        {
            var match = CosmosLegacyTagRowMatcher.Classify(row, derived, out var detail);
            switch (match)
            {
                case LegacyRowMatch.Corrupt:
                    state.Add(
                        new CosmosTagRepairFinding(
                            CosmosTagRepairCategory.Corrupt,
                            eventId,
                            tag,
                            cosmosEvent.SortableUniqueId,
                            detail));
                    return CosmosTagRepairCategory.Corrupt;
                case LegacyRowMatch.LegacyPresent:
                    legacyRows.Add(row);
                    break;
                default:
                    // NotOurKey: the row indexes a different event. Ignore it.
                    break;
            }
        }

        if (legacyRows.Count == 0)
        {
            state.Add(
                new CosmosTagRepairFinding(
                    CosmosTagRepairCategory.Missing,
                    eventId,
                    tag,
                    cosmosEvent.SortableUniqueId));
            return CosmosTagRepairCategory.Missing;
        }

        if (legacyRows.Count > 1)
        {
            // Residue of a pre-SEK-G2 re-execution, which minted a fresh id every time. Reporting is all
            // this service may do — removing one of them is destructive, and that is SEK-G4b's call.
            state.Add(
                new CosmosTagRepairFinding(
                    CosmosTagRepairCategory.Duplicate,
                    eventId,
                    tag,
                    cosmosEvent.SortableUniqueId,
                    $"{legacyRows.Count} legacy rows index this key: ids [{string.Join(", ", legacyRows.Select(row => row.Id))}]. " +
                    "Reported only; reduction is out of scope for this service."));
            return CosmosTagRepairCategory.Duplicate;
        }

        state.Add(
            new CosmosTagRepairFinding(
                CosmosTagRepairCategory.LegacyPresent,
                eventId,
                tag,
                cosmosEvent.SortableUniqueId,
                CosmosLegacyTagRowMatcher.DescribeLegacyDifferences(legacyRows[0], derived)));
        return CosmosTagRepairCategory.LegacyPresent;
    }

    private async Task RepairMissingKeyAsync(
        CosmosTag derived,
        Guid eventId,
        string tag,
        CosmosEvent cosmosEvent,
        CosmosTagRepairOptions options,
        RepairState state,
        CancellationToken cancellationToken)
    {
        var (created, requestCharge) = await ThrottleAware.ExecuteAsync(
            () => _tagStore.TryCreateRowAsync(derived.Pk, derived, cancellationToken),
            options.MaxThrottleRetries,
            cancellationToken).ConfigureAwait(false);

        state.AddRequestCharge(requestCharge);

        if (created)
        {
            state.MarkRepaired();
            if (_logger != null)
                LogRepairedRow(_logger, tag, eventId, null);
            return;
        }

        // A normal write landed between our classification and our repair. That is expected, not an error:
        // the row it wrote is derived from the same event, so it is the row we were about to write.
        var existing = await ThrottleAware.ExecuteAsync(
            () => _tagStore.TryReadRowAsync(derived.Pk, derived.Id, cancellationToken),
            options.MaxThrottleRetries,
            cancellationToken).ConfigureAwait(false);

        if (existing != null && CosmosTagIdentity.ContentEquals(derived, existing))
        {
            state.MarkPresentAfterRace();
            return;
        }

        state.Add(
            new CosmosTagRepairFinding(
                CosmosTagRepairCategory.Corrupt,
                eventId,
                tag,
                cosmosEvent.SortableUniqueId,
                existing == null
                    ? "the row could neither be created nor read back; the tags container is being modified concurrently"
                    : $"a row appeared at the derived id that disagrees with the event. Expected " +
                    $"[{CosmosTagIdentity.Describe(derived)}], actual [{CosmosTagIdentity.Describe(existing)}]"));
    }

    private static readonly Action<ILogger, string, Guid, Exception?> LogRepairedRow =
        LoggerMessage.Define<string, Guid>(
            LogLevel.Information,
            new EventId(1, nameof(LogRepairedRow)),
            "Repaired missing tag row for tag '{Tag}', event {EventId}");

    /// <summary>
    ///     Accumulates one run's counts and findings. Mutated from the per-key tasks, hence the lock.
    /// </summary>
    private sealed class RepairState
    {
        private readonly List<CosmosTagRepairFinding> _findings = new();
        private readonly Lock _gate = new();
        private readonly CosmosTagRepairOptions _options;

        public RepairState(CosmosTagRepairOptions options) => _options = options;

        public int EventsScanned { get; private set; }
        public string? LastSortableUniqueId { get; private set; }

        private int _keysScanned;
        private int _present;
        private int _missing;
        private int _repaired;
        private int _legacyPresent;
        private int _duplicate;
        private int _corrupt;
        private int _overflow;
        private double _requestCharge;

        public void MarkEventScanned(string sortableUniqueId)
        {
            lock (_gate)
            {
                EventsScanned++;
                LastSortableUniqueId = sortableUniqueId;
            }
        }

        public void MarkKeyScanned()
        {
            lock (_gate)
            {
                _keysScanned++;
            }
        }

        public void MarkPresent()
        {
            lock (_gate)
            {
                _present++;
            }
        }

        /// <summary>A concurrent writer wrote the row we were about to write. It is present, not repaired.</summary>
        public void MarkPresentAfterRace()
        {
            lock (_gate)
            {
                _present++;
            }
        }

        public void MarkRepaired()
        {
            lock (_gate)
            {
                _repaired++;
            }
        }

        public void AddRequestCharge(double charge)
        {
            lock (_gate)
            {
                _requestCharge += charge;
            }
        }

        public void Add(CosmosTagRepairFinding finding)
        {
            lock (_gate)
            {
                switch (finding.Category)
                {
                    case CosmosTagRepairCategory.Missing:
                        _missing++;
                        break;
                    case CosmosTagRepairCategory.LegacyPresent:
                        _legacyPresent++;
                        break;
                    case CosmosTagRepairCategory.Duplicate:
                        _duplicate++;
                        break;
                    case CosmosTagRepairCategory.Corrupt:
                        _corrupt++;
                        break;
                    case CosmosTagRepairCategory.Overflow:
                        _overflow++;
                        break;
                    default:
                        // Present.
                        _present++;
                        break;
                }

                // Counts are exact; only the detail list is capped.
                if (_findings.Count < _options.MaxFindings)
                {
                    _findings.Add(finding);
                }
            }
        }

        public CosmosTagRepairReport ToReport(bool hasMore, string? checkpoint)
        {
            lock (_gate)
            {
                return new CosmosTagRepairReport
                {
                    EventsScanned = EventsScanned,
                    KeysScanned = _keysScanned,
                    Present = _present,
                    Missing = _missing,
                    Repaired = _repaired,
                    LegacyPresent = _legacyPresent,
                    Duplicate = _duplicate,
                    Corrupt = _corrupt,
                    Overflow = _overflow,
                    RequestCharge = _requestCharge,
                    DryRun = _options.DryRun,
                    HasMore = hasMore,
                    Checkpoint = checkpoint,
                    Findings = _findings.ToList()
                };
            }
        }
    }
}
