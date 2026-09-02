using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Postgres.DbModels;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Tags;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
namespace Sekiban.Dcb.Postgres;

public class PostgresEventStore : IHotEventStore, ISerializableEventStreamReader, IStorageDurabilityDescriptorProvider,
    IConditionalEventStore, IExpectedTagPositionEventStore, IWriteConditionCapabilityProvider,
    IStreamingTaggedSerializableEventStore, ITaggedStreamCapabilityProvider
{
    private const string ConditionalProviderName = "Postgres";

    /// <summary>
    ///     The name of the events-table primary key <c>(ServiceId, Id)</c>. Only a 23505 on THIS constraint is the
    ///     deterministic claim collision; an unrelated unique-violation (a tag or other constraint) preserves its original
    ///     failure and is never misrouted to winner classification.
    /// </summary>
    private const string EventsPrimaryKeyConstraint = "PK_dcb_events";

    /// <summary>Events land in Postgres and survive this process.</summary>
    public StorageDurabilityDescriptor DescribeStorage() =>
        new(StorageDurability.Durable, "Postgres");

    private readonly ConditionalAppendCoordinator _conditionalAppend;

    /// <summary>
    ///     Test seam ONLY (never set in production): invoked immediately AFTER the conditional claim durably commits, to
    ///     simulate the response/return being lost (transport error / cancellation) while the write is already durable.
    /// </summary>
    internal Func<Task>? AfterConditionalCommitHook { get; set; }

    /// <summary>
    ///     Test-only fault / overlap seam invoked at each fixed real PostgreSQL protocol milestone (before lazy insertion;
    ///     after locking; after reconciliation; after event DML; after tag DML; after head advancement). It deliberately
    ///     surrounds the production SQL and transaction rather than replacing it with a test-owned lock or collection.
    /// </summary>
    internal Func<Task>? TagHeadProtocolHook { get; set; }

    /// <summary>
    ///     SEK-G16 conditional (unique-key) append. The claim event is inserted under the deterministic id, so the
    ///     existing <c>(ServiceId, Id)</c> primary key is the uniqueness primitive — no schema change. A duplicate raises
    ///     SQLSTATE 23505 (unique_violation), which is classified by fingerprint against the stored winner (the real
    ///     PostgresException is preserved as the diagnostic cause on a key-reuse conflict). The unconditional write path
    ///     and the retrying execution strategy it uses are untouched; the conditional path uses a plain transaction
    ///     because a 23505 is a genuine conflict, not a transient to retry.
    /// </summary>
    public Task<ResultBox<ConditionalAppendReceipt>> AppendIfUniqueAsync(
        ConditionalAppendRequest request,
        CancellationToken cancellationToken = default) =>
        _conditionalAppend.AppendIfUniqueAsync(request, cancellationToken);

    /// <inheritdoc />
    public WriteConditionCapabilityDescriptor DescribeWriteConditions() =>
        WriteConditionCapabilityDescriptor.Supporting(
            ConditionalProviderName,
            WriteConditionKind.SingleEventUniqueKey,
            WriteConditionKind.ExpectedTagPosition);

    /// <summary>Postgres pushes tagged-stream bounds and ordering into its indexed query.</summary>
    public TaggedStreamCapabilityDescriptor DescribeTaggedStream() =>
        TaggedStreamCapabilityDescriptor.Native("Postgres");

    private async Task<ConditionalWriteOutcome> TryWriteConditionalClaimAsync(
        Guid deterministicId,
        SerializableEvent claimEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            // This is intentionally the same private seam used by both ordinary batch writers. A successful conditional
            // claim can therefore never be a tagged event invisible to the durable head protocol.
            var result = await WriteSerializableBatchThroughCanonicalHeadSeamAsync(
                [claimEvent],
                specification: null,
                writer: TagHeadWriter.ConditionalClaim,
                useExecutionStrategy: false,
                cancellationToken);
            if (!result.IsSuccess)
            {
                throw result.GetException();
            }

            // The claim is now durably committed. A failure past this point is a LOST RESPONSE, not a failed write: signal
            // it as the post-commit ambiguity marker so the shared orchestrator resolves it authoritatively rather than
            // surfacing a raw transport error. (The seam is test-only; production has no hook.)
            if (AfterConditionalCommitHook is not null)
            {
                try
                {
                    await AfterConditionalCommitHook();
                }
                catch (Exception ex)
                {
                    throw new PostCommitResponseLostException(ex);
                }
            }
            return ConditionalWriteOutcome.Wrote();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: EventsPrimaryKeyConstraint
        })
        {
            return ConditionalWriteOutcome.Conflict(ex);
        }
    }

    private async Task<SerializableEvent?> ReadConditionalWinnerAsync(Guid deterministicId, CancellationToken cancellationToken)
    {
        var serviceId = CurrentServiceId;
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var dbEvent = await context.Events.AsNoTracking()
            .FirstOrDefaultAsync(e => e.ServiceId == serviceId && e.Id == deterministicId, cancellationToken);
        if (dbEvent is null)
        {
            return null;
        }

        var tags = JsonSerializer.Deserialize<List<string>>(dbEvent.Tags) ?? new List<string>();
        return new SerializableEvent(
            Encoding.UTF8.GetBytes(dbEvent.Payload),
            dbEvent.SortableUniqueId,
            dbEvent.Id,
            new EventMetadata(dbEvent.CausationId ?? "", dbEvent.CorrelationId ?? "", dbEvent.ExecutedUser ?? ""),
            tags,
            dbEvent.EventType);
    }

    private readonly IDbContextFactory<SekibanDcbDbContext> _contextFactory;
    private readonly IEventTypes _eventTypes;
    private readonly IServiceIdProvider _serviceIdProvider;
    private readonly ILogger<PostgresEventStore> _logger;

    public PostgresEventStore(
        IDbContextFactory<SekibanDcbDbContext> contextFactory,
        IEventTypes eventTypes,
        IServiceIdProvider serviceIdProvider,
        ILogger<PostgresEventStore>? logger = null)
    {
        _contextFactory = contextFactory;
        _eventTypes = eventTypes;
        _serviceIdProvider = serviceIdProvider ?? throw new ArgumentNullException(nameof(serviceIdProvider));
        _logger = logger ?? NullLogger<PostgresEventStore>.Instance;
        _conditionalAppend = new ConditionalAppendCoordinator(
            ConditionalProviderName, () => CurrentServiceId, _eventTypes,
            TryWriteConditionalClaimAsync, ReadConditionalWinnerAsync);
    }

    private string CurrentServiceId => _serviceIdProvider.GetCurrentServiceId();

    public async Task<ResultBox<IEnumerable<Event>>> ReadAllEventsAsync(SortableUniqueId? since = null, int? maxCount = null)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var serviceId = CurrentServiceId;

            var query = context.Events.AsQueryable().Where(e => e.ServiceId == serviceId);

            if (since != null)
            {
                query = query.Where(e => string.Compare(e.SortableUniqueId, since.Value) > 0);
            }

            var orderedQuery = query.OrderBy(e => e.SortableUniqueId);
            var limitedQuery = maxCount.HasValue
                ? orderedQuery.Take(maxCount.Value)
                : orderedQuery;

            var dbEvents = await limitedQuery.ToListAsync();

            var events = new List<Event>();
            foreach (var dbEvent in dbEvents)
            {
                var payloadResult = DeserializeEventPayload(dbEvent.EventType, dbEvent.Payload);
                if (!payloadResult.IsSuccess)
                {
                    return ResultBox.Error<IEnumerable<Event>>(payloadResult.GetException());
                }

                events.Add(dbEvent.ToEvent(payloadResult.GetValue()));
            }

            return ResultBox.FromValue<IEnumerable<Event>>(events);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IEnumerable<Event>>(ex);
        }
    }

    public async Task<ResultBox<IEnumerable<Event>>> ReadEventsByTagAsync(ITag tag, SortableUniqueId? since = null)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var serviceId = CurrentServiceId;

            var tagString = tag.GetTag();

            // Query via dcb_tags table (indexed) instead of dcb_events.Tags jsonb scan
            var tagQuery = context.Tags.Where(t =>
                t.ServiceId == serviceId && t.Tag == tagString);

            if (since != null)
            {
                var sinceValue = since.Value;
                tagQuery = tagQuery.Where(t => string.Compare(t.SortableUniqueId, sinceValue) > 0);
            }

            var dbEvents = await tagQuery
                .OrderBy(t => t.SortableUniqueId)
                .Join(
                    context.Events,
                    t => new { t.ServiceId, EventId = t.EventId },
                    e => new { e.ServiceId, EventId = e.Id },
                    (t, e) => e)
                .ToListAsync();

            var events = new List<Event>();
            foreach (var dbEvent in dbEvents)
            {
                var payloadResult = DeserializeEventPayload(dbEvent.EventType, dbEvent.Payload);
                if (!payloadResult.IsSuccess)
                {
                    return ResultBox.Error<IEnumerable<Event>>(payloadResult.GetException());
                }

                events.Add(dbEvent.ToEvent(payloadResult.GetValue()));
            }

            return ResultBox.FromValue<IEnumerable<Event>>(events);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IEnumerable<Event>>(ex);
        }
    }

    public async Task<ResultBox<Event>> ReadEventAsync(Guid eventId)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var serviceId = CurrentServiceId;

            var dbEvent = await context.Events.FirstOrDefaultAsync(e =>
                e.ServiceId == serviceId &&
                e.Id == eventId);

            if (dbEvent == null)
            {
                return ResultBox.Error<Event>(new Exception($"Event with ID {eventId} not found"));
            }

            var payloadResult = DeserializeEventPayload(dbEvent.EventType, dbEvent.Payload);
            if (!payloadResult.IsSuccess)
            {
                return ResultBox.Error<Event>(payloadResult.GetException());
            }

            return ResultBox.FromValue(dbEvent.ToEvent(payloadResult.GetValue()));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<Event>(ex);
        }
    }

    public async Task<ResultBox<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
        WriteEventsAsync(IEnumerable<Event> events)
    {
        try
        {
            var typedEvents = events.ToList();
            var serializableEvents = typedEvents
                .Select(ev => ev.ToSerializableEvent(_eventTypes))
                .ToList();
            var write = await WriteSerializableBatchThroughCanonicalHeadSeamAsync(
                serializableEvents,
                specification: null,
                writer: TagHeadWriter.TypedBatch,
                useExecutionStrategy: true,
                CancellationToken.None);
            if (!write.IsSuccess)
            {
                return ResultBox.Error<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>(
                    write.GetException());
            }

            return ResultBox.FromValue(
                (Events: (IReadOnlyList<Event>)typedEvents,
                    TagWrites: write.GetValue().TagWrites));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WriteEventsAsync failed");
            return ResultBox.Error<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>(ex);
        }
    }

    public async Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var serviceId = CurrentServiceId;

            var tagString = tag.GetTag();

            // Get all tag entries for this tag
            var tags = await context.Tags
                .Where(t => t.ServiceId == serviceId && t.Tag == tagString)
                .OrderBy(t => t.SortableUniqueId)
                .ToListAsync();

            var tagStreams = new List<TagStream>();
            foreach (var dbTag in tags)
            {
                tagStreams.Add(new TagStream(dbTag.Tag, dbTag.EventId, dbTag.SortableUniqueId));
            }

            return ResultBox.FromValue<IEnumerable<TagStream>>(tagStreams);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IEnumerable<TagStream>>(ex);
        }
    }

    public async Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var serviceId = CurrentServiceId;

            var tagString = tag.GetTag();
            var tagGroup = tag.GetTagGroup();
            var tagContent = tagString.Substring(tagGroup.Length + 1);

            // Get the latest tag entry
            var dbTag = await context
                .Tags
                .Where(t => t.ServiceId == serviceId && t.Tag == tagString)
                .OrderByDescending(t => t.SortableUniqueId)
                .FirstOrDefaultAsync();

            if (dbTag == null)
            {
                // Return empty tag state
                return ResultBox.FromValue(
                    new TagState(
                        new EmptyTagStatePayload(),
                        0,
                        string.Empty,
                        tagGroup,
                        tagContent,
                        string.Empty,
                        string.Empty));
            }

            // Return a simple tag state with just the latest sortable unique ID
            // The actual state would be computed by projectors
            return ResultBox.FromValue(
                new TagState(
                    new EmptyTagStatePayload(),
                    0,
                    dbTag.SortableUniqueId,
                    tagGroup,
                    tagContent,
                    string.Empty,
                    string.Empty));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<TagState>(ex);
        }
    }

    public async Task<ResultBox<bool>> TagExistsAsync(ITag tag)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var serviceId = CurrentServiceId;

            var tagString = tag.GetTag();
            var exists = await context.Tags.AnyAsync(t => t.ServiceId == serviceId && t.Tag == tagString);

            return ResultBox.FromValue(exists);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<bool>(ex);
        }
    }

    public async Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var serviceId = CurrentServiceId;

            var query = context.Events.AsQueryable().Where(e => e.ServiceId == serviceId);
            if (since != null)
            {
                query = query.Where(e => string.Compare(e.SortableUniqueId, since.Value) > 0);
            }

            var count = await query.LongCountAsync();
            return ResultBox.FromValue(count);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<long>(ex);
        }
    }

    public async Task<ResultBox<string>> GetLatestSortableUniqueIdAsync()
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var serviceId = CurrentServiceId;

            var latest = await context.Events
                .Where(e => e.ServiceId == serviceId)
                .OrderByDescending(e => e.SortableUniqueId)
                .Select(e => e.SortableUniqueId)
                .FirstOrDefaultAsync();

            return ResultBox.FromValue(latest ?? string.Empty);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<string>(ex);
        }
    }

    public async Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var serviceId = CurrentServiceId;

            var query = context.Tags.AsQueryable().Where(t => t.ServiceId == serviceId);
            if (!string.IsNullOrEmpty(tagGroup))
            {
                query = query.Where(t => t.TagGroup == tagGroup);
            }

            // Group by tag and get aggregated info
            var tagInfos = await query
                .GroupBy(t => new { t.Tag, t.TagGroup })
                .Select(g => new
                {
                    g.Key.Tag,
                    g.Key.TagGroup,
                    EventCount = g.Count(),
                    FirstSortableUniqueId = g.Min(t => t.SortableUniqueId),
                    LastSortableUniqueId = g.Max(t => t.SortableUniqueId),
                    FirstEventAt = g.Min(t => t.CreatedAt),
                    LastEventAt = g.Max(t => t.CreatedAt)
                })
                .OrderBy(t => t.TagGroup)
                .ThenBy(t => t.Tag)
                .ToListAsync();

            var result = tagInfos.Select(t => new TagInfo(
                t.Tag,
                t.TagGroup,
                t.EventCount,
                t.FirstSortableUniqueId,
                t.LastSortableUniqueId,
                t.FirstEventAt,
                t.LastEventAt));

            return ResultBox.FromValue(result.AsEnumerable());
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IEnumerable<TagInfo>>(ex);
        }
    }

    public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null)
        => ReadAllSerializableEventsAsync(since, maxCount: null);

    public async Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
        SortableUniqueId? since,
        int? maxCount)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var serviceId = CurrentServiceId;

            var query = context.Events.AsQueryable().Where(e => e.ServiceId == serviceId);

            if (since != null)
            {
                query = query.Where(e => string.Compare(e.SortableUniqueId, since.Value) > 0);
            }

            var orderedQuery = query.OrderBy(e => e.SortableUniqueId);
            var limitedQuery = maxCount.HasValue
                ? orderedQuery.Take(maxCount.Value)
                : orderedQuery;

            var dbEvents = await limitedQuery.ToListAsync();

            var events = dbEvents.Select(dbEvent => new SerializableEvent(
                Encoding.UTF8.GetBytes(dbEvent.Payload),
                dbEvent.SortableUniqueId,
                dbEvent.Id,
                new EventMetadata(dbEvent.CausationId ?? "", dbEvent.CorrelationId ?? "", dbEvent.ExecutedUser ?? ""),
                JsonSerializer.Deserialize<List<string>>(dbEvent.Tags) ?? new List<string>(),
                dbEvent.EventType));

            return ResultBox.FromValue<IEnumerable<SerializableEvent>>(events);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IEnumerable<SerializableEvent>>(ex);
        }
    }

    public async Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var serviceId = CurrentServiceId;

            var dbEvent = await context.Events.FirstOrDefaultAsync(e =>
                e.ServiceId == serviceId &&
                e.Id == eventId);

            if (dbEvent == null)
            {
                return ResultBox.Error<SerializableEvent>(new Exception($"Event with ID {eventId} not found"));
            }

            return ResultBox.FromValue(new SerializableEvent(
                Encoding.UTF8.GetBytes(dbEvent.Payload),
                dbEvent.SortableUniqueId,
                dbEvent.Id,
                new EventMetadata(dbEvent.CausationId ?? "", dbEvent.CorrelationId ?? "", dbEvent.ExecutedUser ?? ""),
                JsonSerializer.Deserialize<List<string>>(dbEvent.Tags) ?? new List<string>(),
                dbEvent.EventType));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<SerializableEvent>(ex);
        }
    }

    public async IAsyncEnumerable<SerializableEvent> StreamAllSerializableEventsAsync(
        SortableUniqueId? since,
        int? maxCount,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var serviceId = CurrentServiceId;

        var query = context.Events
            .AsNoTracking()
            .Where(e => e.ServiceId == serviceId);

        if (since != null)
        {
            query = query.Where(e => string.Compare(e.SortableUniqueId, since.Value) > 0);
        }

        var orderedQuery = query.OrderBy(e => e.SortableUniqueId);
        var limitedQuery = maxCount.HasValue
            ? orderedQuery.Take(maxCount.Value)
            : orderedQuery;

        await foreach (var dbEvent in limitedQuery.AsAsyncEnumerable().WithCancellation(ct))
        {
            yield return new SerializableEvent(
                Encoding.UTF8.GetBytes(dbEvent.Payload),
                dbEvent.SortableUniqueId,
                dbEvent.Id,
                new EventMetadata(dbEvent.CausationId ?? "", dbEvent.CorrelationId ?? "", dbEvent.ExecutedUser ?? ""),
                JsonSerializer.Deserialize<List<string>>(dbEvent.Tags) ?? new List<string>(),
                dbEvent.EventType);
        }
    }

    public async Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(ITag tag, SortableUniqueId? since = null)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var serviceId = CurrentServiceId;

            var tagString = tag.GetTag();

            // Query via dcb_tags table (indexed) instead of dcb_events.Tags jsonb scan
            var tagQuery = context.Tags.Where(t =>
                t.ServiceId == serviceId && t.Tag == tagString);

            if (since != null)
            {
                var sinceValue = since.Value;
                tagQuery = tagQuery.Where(t => string.Compare(t.SortableUniqueId, sinceValue) > 0);
            }

            var dbEvents = await tagQuery
                .OrderBy(t => t.SortableUniqueId)
                .Join(
                    context.Events,
                    t => new { t.ServiceId, EventId = t.EventId },
                    e => new { e.ServiceId, EventId = e.Id },
                    (t, e) => e)
                .ToListAsync();

            var events = dbEvents.Select(dbEvent => new SerializableEvent(
                Encoding.UTF8.GetBytes(dbEvent.Payload),
                dbEvent.SortableUniqueId,
                dbEvent.Id,
                new EventMetadata(dbEvent.CausationId ?? "", dbEvent.CorrelationId ?? "", dbEvent.ExecutedUser ?? ""),
                JsonSerializer.Deserialize<List<string>>(dbEvent.Tags) ?? new List<string>(),
                dbEvent.EventType));

            return ResultBox.FromValue<IEnumerable<SerializableEvent>>(events);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IEnumerable<SerializableEvent>>(ex);
        }
    }

    /// <summary>
    ///     Streams a single tag without materializing the tagged history. The context remains alive until the callback
    ///     finishes, and both bounds are retained in the database query.
    /// </summary>
    public async Task<ResultBox<SerializableEventStreamReadResult>> StreamSerializableEventsByTagAsync(
        ITag tag,
        SortableUniqueId? since,
        SortableUniqueId? until,
        Func<SerializableEvent, ValueTask> onEvent,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var serviceId = CurrentServiceId;
            var tagString = tag.GetTag();

            var tagQuery = context.Tags
                .AsNoTracking()
                .Where(t => t.ServiceId == serviceId && t.Tag == tagString);

            if (since != null)
            {
                var sinceValue = since.Value;
                tagQuery = tagQuery.Where(t => string.Compare(t.SortableUniqueId, sinceValue) > 0);
            }

            if (until != null)
            {
                var untilValue = until.Value;
                tagQuery = tagQuery.Where(t => string.Compare(t.SortableUniqueId, untilValue) <= 0);
            }

            var query = tagQuery
                .Join(
                    context.Events.AsNoTracking(),
                    t => new { t.ServiceId, EventId = t.EventId },
                    e => new { e.ServiceId, EventId = e.Id },
                    (t, e) => new { Tag = t, Event = e })
                .OrderBy(row => row.Tag.SortableUniqueId)
                .Select(row => row.Event);

            var count = 0;
            string? lastSortableUniqueId = null;
            await foreach (var dbEvent in query.AsAsyncEnumerable().WithCancellation(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await onEvent(new SerializableEvent(
                    Encoding.UTF8.GetBytes(dbEvent.Payload),
                    dbEvent.SortableUniqueId,
                    dbEvent.Id,
                    new EventMetadata(dbEvent.CausationId ?? "", dbEvent.CorrelationId ?? "", dbEvent.ExecutedUser ?? ""),
                    JsonSerializer.Deserialize<List<string>>(dbEvent.Tags) ?? new List<string>(),
                    dbEvent.EventType));
                cancellationToken.ThrowIfCancellationRequested();
                count++;
                lastSortableUniqueId = dbEvent.SortableUniqueId;
            }

            return ResultBox.FromValue(new SerializableEventStreamReadResult(count, lastSortableUniqueId));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ResultBox.Error<SerializableEventStreamReadResult>(ex);
        }
    }

    public async Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
        WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events)
    {
        try
        {
            var write = await WriteSerializableBatchThroughCanonicalHeadSeamAsync(
                events.ToList(),
                specification: null,
                writer: TagHeadWriter.SerializedBatch,
                useExecutionStrategy: true,
                CancellationToken.None);
            return !write.IsSuccess
                ? ResultBox.Error<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>(
                    write.GetException())
                : ResultBox.FromValue((write.GetValue().Events, write.GetValue().TagWrites));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WriteSerializableEventsAsync failed");
            return ResultBox.Error<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<ResultBox<bool>> EnsureExpectedTagPositionEnforcementEnabledAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var serviceId = CurrentServiceId;
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var enabled = await context.TagHeadEnablementEpochs.AsNoTracking()
                .AnyAsync(epoch => epoch.ServiceId == serviceId, cancellationToken);
            return enabled
                ? ResultBox.FromValue(true)
                : ResultBox.Error<bool>(new TagHeadEnforcementNotEnabledException(serviceId));
        }
        catch (Exception ex)
        {
            // Deliberately no fallback DDL or schema creation: an unprovisioned runtime must fail closed (normally 42P01).
            return ResultBox.Error<bool>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<ResultBox<ExpectedTagPositionWriteResult>> WriteSerializableEventsWithExpectedTagPositionsAsync(
        IReadOnlyList<SerializableEvent> events,
        ExpectedTagPositionSpecification specification,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(specification);
            specification.ValidateEntryShapes(CurrentServiceId);
            if (specification.RequiresEnforcement)
            {
                var enabled = await EnsureExpectedTagPositionEnforcementEnabledAsync(cancellationToken);
                if (!enabled.IsSuccess)
                {
                    return ResultBox.Error<ExpectedTagPositionWriteResult>(enabled.GetException());
                }
            }

            return await WriteSerializableBatchThroughCanonicalHeadSeamAsync(
                events,
                specification,
                TagHeadWriter.ExpectedPositionBatch,
                useExecutionStrategy: true,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WriteSerializableEventsWithExpectedTagPositionsAsync failed");
            return ResultBox.Error<ExpectedTagPositionWriteResult>(ex);
        }
    }

    /// <summary>
    ///     The one authoritative PostgreSQL tagged-write seam. Typed, serialized, conditional-claim, and expected-position
    ///     writers all call this method. The transaction deliberately owns lazy creation, canonical locking,
    ///     reconciliation, command rows, tag-index rows, and per-tag head advancement together.
    /// </summary>
    private async Task<ResultBox<ExpectedTagPositionWriteResult>> WriteSerializableBatchThroughCanonicalHeadSeamAsync(
        IReadOnlyList<SerializableEvent> events,
        ExpectedTagPositionSpecification? specification,
        TagHeadWriter writer,
        bool useExecutionStrategy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (specification is not null)
        {
            // Batch ordering is fully known before a transaction or a lazy head row exists.
            ValidateStrictBatchOrder(events);
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        if (!useExecutionStrategy)
        {
            return await PersistThroughCanonicalHeadSeamAsync(context, events, specification, writer, cancellationToken);
        }

        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            () => PersistThroughCanonicalHeadSeamAsync(context, events, specification, writer, cancellationToken));
    }

    private async Task<ResultBox<ExpectedTagPositionWriteResult>> PersistThroughCanonicalHeadSeamAsync(
        SekibanDcbDbContext context,
        IReadOnlyList<SerializableEvent> events,
        ExpectedTagPositionSpecification? specification,
        TagHeadWriter writer,
        CancellationToken cancellationToken)
    {
        var serviceId = CurrentServiceId;
        var keys = events
            .SelectMany(@event => @event.Tags)
            .Distinct(StringComparer.Ordinal)
            .Select(tag => new TagHeadKey(serviceId, tag))
            .OrderBy(key => key.ServiceId, StringComparer.Ordinal)
            .ThenBy(key => key.Tag, StringComparer.Ordinal)
            .ToArray();

        if (specification is not null)
        {
            specification.ValidateEntryShapes(serviceId);
            var presentTags = keys.Select(key => key.Tag).ToHashSet(StringComparer.Ordinal);
            var unknown = specification.Entries.Select(entry => entry.Tag)
                .Where(tag => !presentTags.Contains(tag))
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (unknown.Length > 0)
            {
                return ResultBox.Error<ExpectedTagPositionWriteResult>(
                    new TagHeadExpectationValidationException(
                        $"Expected tag-head entries are not present in this batch: {string.Join(", ", unknown)}."));
            }
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var created = new HashSet<TagHeadKey>();
        var heads = new Dictionary<TagHeadKey, string?>();
        var repairs = new List<TagHeadRepair>();
        try
        {
            // Phase 1: lazy rows are inserted in the exact same ordinal ordering as later FOR UPDATE acquisition. Never
            // let caller/event order reach the unique index first: reversed batches otherwise form a real 40P01 cycle.
            await InvokeTagHeadProtocolHookAsync();
            foreach (var key in keys)
            {
                var inserted = await ExecuteNonQueryAsync(
                    context,
                    transaction,
                    "INSERT INTO dcb_tag_heads (\"ServiceId\", \"Tag\", \"HeadPosition\") " +
                    "VALUES (@serviceId, @tag, NULL) ON CONFLICT (\"ServiceId\", \"Tag\") DO NOTHING",
                    cancellationToken,
                    Parameter("serviceId", key.ServiceId),
                    Parameter("tag", key.Tag));
                if (inserted == 1)
                {
                    created.Add(key);
                }
            }

            // Phase 2: acquire every row in exactly the same canonical order. A newly inserted row is bootstraped from
            // dcb_tags while this transaction owns its key; absence is only proven empty after the authoritative MAX.
            foreach (var key in keys)
            {
                var head = await QueryNullableStringAsync(
                    context,
                    transaction,
                    "SELECT \"HeadPosition\" FROM dcb_tag_heads " +
                    "WHERE \"ServiceId\" = @serviceId AND \"Tag\" = @tag FOR UPDATE",
                    cancellationToken,
                    Parameter("serviceId", key.ServiceId),
                    Parameter("tag", key.Tag));

                if (created.Contains(key))
                {
                    var authoritativeMaximum = await QueryNullableStringAsync(
                        context,
                        transaction,
                        "SELECT MAX(\"SortableUniqueId\") FROM dcb_tags " +
                        "WHERE \"ServiceId\" = @serviceId AND \"Tag\" = @tag",
                        cancellationToken,
                        Parameter("serviceId", key.ServiceId),
                        Parameter("tag", key.Tag));
                    if (authoritativeMaximum is not null)
                    {
                        await UpdateHeadAsync(context, transaction, key, authoritativeMaximum, cancellationToken);
                        head = authoritativeMaximum;
                    }
                    // A persisted null head is the explicit, transactionally proven-empty representation.
                }

                heads[key] = head;
            }
            await InvokeTagHeadProtocolHookAsync();

            // Phase 3: reconciliation is always service-scoped. It observes only rows newer than this durable head and
            // records / repairs them before expected-head comparison or command DML.
            foreach (var key in keys)
            {
                var priorHead = heads[key];
                var observedMaximum = await QueryNullableStringAsync(
                    context,
                    transaction,
                    "SELECT MAX(\"SortableUniqueId\") FROM dcb_tags " +
                    "WHERE \"ServiceId\" = @serviceId AND \"Tag\" = @tag " +
                    "AND (@headPosition IS NULL OR \"SortableUniqueId\" > @headPosition)",
                    cancellationToken,
                    Parameter("serviceId", key.ServiceId),
                    Parameter("tag", key.Tag),
                    NullableTextParameter("headPosition", priorHead));
                if (observedMaximum is null)
                {
                    continue;
                }

                await InsertViolationIdempotentlyAsync(
                    context, transaction, key, priorHead, observedMaximum, writer, cancellationToken);
                await UpdateHeadAsync(context, transaction, key, observedMaximum, cancellationToken);
                heads[key] = observedMaximum;
                repairs.Add(new TagHeadRepair(key, priorHead, observedMaximum));
            }
            await InvokeTagHeadProtocolHookAsync();

            // Phase 4: ALL expectations compare after EVERY key has reconciled. A combined multi-tag conflict contains a
            // complete pair set, never a first-failure subset.
            if (specification is not null)
            {
                var pairs = specification.Entries
                    .OrderBy(entry => entry.ServiceId, StringComparer.Ordinal)
                    .ThenBy(entry => entry.Tag, StringComparer.Ordinal)
                    .Select(entry => new TagHeadExpectedObserved(
                        entry.ServiceId,
                        entry.Tag,
                        entry.Expectation,
                        heads[new TagHeadKey(entry.ServiceId, entry.Tag)]))
                    .ToArray();
                var mismatched = pairs.Any(pair => !ExpectationMatches(pair.Expected, pair.ObservedPosition));
                if (mismatched)
                {
                    var conflict = new ExpectedTagPositionConflictException(pairs);
                    if (repairs.Count == 0)
                    {
                        // Ordinary stale/mismatch: no repair evidence exists, so all lazy rows and all mutation roll back.
                        await transaction.RollbackAsync(cancellationToken);
                        return ResultBox.Error<ExpectedTagPositionWriteResult>(conflict);
                    }

                    // Repair-only conflict: preserve ONLY repaired heads and their idempotent audit rows. In particular a
                    // third unrelated lazy tag C cannot leak out of this transaction merely because tags A/B conflicted.
                    foreach (var key in created.Where(key => repairs.All(repair => repair.Key != key)))
                    {
                        await ExecuteNonQueryAsync(
                            context,
                            transaction,
                            "DELETE FROM dcb_tag_heads WHERE \"ServiceId\" = @serviceId AND \"Tag\" = @tag",
                            cancellationToken,
                            Parameter("serviceId", key.ServiceId),
                            Parameter("tag", key.Tag));
                    }
                    await transaction.CommitAsync(cancellationToken);
                    LogCommittedRepairs(repairs, writer);
                    return ResultBox.Error<ExpectedTagPositionWriteResult>(conflict);
                }

                // The second half of the position rule depends on the locked/reconciled prior heads. It remains before
                // event/tag DML and an invalid batch rolls every lazy/repair mutation back.
                ValidatePositionsAgainstHeads(events, heads, serviceId);
            }

            // Phase 5: command DML is deliberately split so fault-injection tests can prove transaction atomicity from a
            // fresh connection after event insertion, tag-index insertion, and head update respectively.
            foreach (var @event in events)
            {
                context.Events.Add(ToDbEvent(@event, serviceId));
            }
            await context.SaveChangesAsync(cancellationToken);
            await InvokeTagHeadProtocolHookAsync();

            var tagWriteResults = new List<TagWriteResult>();
            foreach (var @event in events)
            {
                foreach (var tagString in @event.Tags)
                {
                    var tagGroup = tagString.Contains(':') ? tagString.Split(':')[0] : tagString;
                    context.Tags.Add(DbTag.FromEventTag(
                        tagString,
                        tagGroup,
                        @event.SortableUniqueIdValue,
                        @event.Id,
                        @event.EventPayloadName,
                        serviceId));
                    tagWriteResults.Add(new TagWriteResult(tagString, 1, DateTimeOffset.UtcNow));
                }
            }
            await context.SaveChangesAsync(cancellationToken);
            await InvokeTagHeadProtocolHookAsync();

            foreach (var (key, batchMaximum) in GetPerTagBatchMaximums(events, serviceId))
            {
                // Legacy writers deliberately perform no expectation comparison, but they still must never regress a
                // durable head that an earlier canonical writer (or reconciliation) has already advanced. The protocol's
                // success rule is max(reconciled head, this tag's batch maximum), not "last writer wins".
                var reconciledHead = heads[key];
                if (reconciledHead is null ||
                    StringComparer.Ordinal.Compare(batchMaximum, reconciledHead) > 0)
                {
                    await UpdateHeadAsync(context, transaction, key, batchMaximum, cancellationToken);
                    heads[key] = batchMaximum;
                }
            }
            await InvokeTagHeadProtocolHookAsync();

            await transaction.CommitAsync(cancellationToken);
            LogCommittedRepairs(repairs, writer);
            return ResultBox.FromValue(new ExpectedTagPositionWriteResult(events, tagWriteResults));
        }
        catch
        {
            // A disposed uncommitted Npgsql transaction rolls back too, but make the all-or-nothing boundary explicit for
            // the injected partial-state tests and for a failure before the using scope unwinds.
            if (transaction.GetDbTransaction().Connection is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            throw;
        }
    }

    private static DbEvent ToDbEvent(SerializableEvent @event, string serviceId) => new()
    {
        ServiceId = serviceId,
        Id = @event.Id,
        SortableUniqueId = @event.SortableUniqueIdValue,
        EventType = @event.EventPayloadName,
        Payload = Encoding.UTF8.GetString(@event.Payload),
        Tags = JsonSerializer.Serialize(@event.Tags),
        Timestamp = DateTime.UtcNow,
        CausationId = @event.EventMetadata.CausationId,
        CorrelationId = @event.EventMetadata.CorrelationId,
        ExecutedUser = @event.EventMetadata.ExecutedUser
    };

    private static void ValidateStrictBatchOrder(IReadOnlyList<SerializableEvent> events)
    {
        for (var index = 1; index < events.Count; index++)
        {
            if (StringComparer.Ordinal.Compare(events[index - 1].SortableUniqueIdValue, events[index].SortableUniqueIdValue) >= 0)
            {
                throw new TagHeadPositionValidationException(
                    "Expected-position batches must carry strictly increasing SortableUniqueId values in batch order.");
            }
        }
    }

    private static void ValidatePositionsAgainstHeads(
        IReadOnlyList<SerializableEvent> events,
        IReadOnlyDictionary<TagHeadKey, string?> heads,
        string serviceId)
    {
        foreach (var @event in events)
        {
            foreach (var tag in @event.Tags)
            {
                var priorHead = heads[new TagHeadKey(serviceId, tag)];
                if (priorHead is not null &&
                    StringComparer.Ordinal.Compare(@event.SortableUniqueIdValue, priorHead) <= 0)
                {
                    throw new TagHeadPositionValidationException(
                        $"Event position '{@event.SortableUniqueIdValue}' must exceed the reconciled head '{priorHead}' for tag '{tag}'.");
                }
            }
        }
    }

    private static IEnumerable<KeyValuePair<TagHeadKey, string>> GetPerTagBatchMaximums(
        IReadOnlyList<SerializableEvent> events,
        string serviceId)
    {
        var maxima = new Dictionary<TagHeadKey, string>();
        foreach (var @event in events)
        {
            foreach (var tag in @event.Tags)
            {
                var key = new TagHeadKey(serviceId, tag);
                if (!maxima.TryGetValue(key, out var existing) ||
                    StringComparer.Ordinal.Compare(@event.SortableUniqueIdValue, existing) > 0)
                {
                    maxima[key] = @event.SortableUniqueIdValue;
                }
            }
        }
        return maxima.OrderBy(pair => pair.Key.ServiceId, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Tag, StringComparer.Ordinal);
    }

    private static bool ExpectationMatches(TagHeadExpectation expected, string? observedPosition) => expected.Kind switch
    {
        TagHeadExpectationKind.NoEnforcement => true,
        TagHeadExpectationKind.AssertEmpty => observedPosition is null,
        TagHeadExpectationKind.Exact => StringComparer.Ordinal.Equals(expected.Position, observedPosition),
        _ => false
    };

    private async Task InsertViolationIdempotentlyAsync(
        SekibanDcbDbContext context,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        TagHeadKey key,
        string? previousHead,
        string observedPosition,
        TagHeadWriter writer,
        CancellationToken cancellationToken) =>
        await ExecuteNonQueryAsync(
            context,
            transaction,
            "INSERT INTO dcb_tag_head_violations (\"ServiceId\", \"Tag\", \"PreviousHeadWasEmpty\", " +
            "\"PreviousHeadPosition\", \"ObservedPosition\", \"DetectedAtUtc\", \"DetectingWriter\") " +
            "VALUES (@serviceId, @tag, @wasEmpty, @previous, @observed, @detectedAtUtc, @writer) " +
            "ON CONFLICT (\"ServiceId\", \"Tag\", \"PreviousHeadWasEmpty\", \"PreviousHeadPosition\", \"ObservedPosition\") DO NOTHING",
            cancellationToken,
            Parameter("serviceId", key.ServiceId),
            Parameter("tag", key.Tag),
            Parameter("wasEmpty", previousHead is null),
            Parameter("previous", previousHead ?? string.Empty),
            Parameter("observed", observedPosition),
            Parameter("detectedAtUtc", DateTime.UtcNow),
            Parameter("writer", writer.ToString()));

    private static Task UpdateHeadAsync(
        SekibanDcbDbContext context,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        TagHeadKey key,
        string? position,
        CancellationToken cancellationToken) =>
        ExecuteNonQueryAsync(
            context,
            transaction,
            "UPDATE dcb_tag_heads SET \"HeadPosition\" = @position " +
            "WHERE \"ServiceId\" = @serviceId AND \"Tag\" = @tag",
            cancellationToken,
            Parameter("position", position),
            Parameter("serviceId", key.ServiceId),
            Parameter("tag", key.Tag));

    private static async Task<int> ExecuteNonQueryAsync(
        SekibanDcbDbContext context,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        string commandText,
        CancellationToken cancellationToken,
        params NpgsqlParameter[] parameters)
    {
        await using var command = CreateCommand(context, transaction, commandText, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string?> QueryNullableStringAsync(
        SekibanDcbDbContext context,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        string commandText,
        CancellationToken cancellationToken,
        params NpgsqlParameter[] parameters)
    {
        await using var command = CreateCommand(context, transaction, commandText, parameters);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : (string)value;
    }

    private static NpgsqlCommand CreateCommand(
        SekibanDcbDbContext context,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        string commandText,
        IReadOnlyCollection<NpgsqlParameter> parameters)
    {
        var connection = context.Database.GetDbConnection() as NpgsqlConnection
            ?? throw new InvalidOperationException("PostgresEventStore requires an Npgsql connection.");
        var npgsqlTransaction = transaction.GetDbTransaction() as NpgsqlTransaction
            ?? throw new InvalidOperationException("PostgresEventStore requires an Npgsql transaction.");
        var command = new NpgsqlCommand(commandText, connection, npgsqlTransaction);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }
        return command;
    }

    private static NpgsqlParameter Parameter(string name, object? value) => new(name, value ?? DBNull.Value);

    // PostgreSQL cannot infer a parameter type when a null is used both in an IS NULL predicate and a comparison. The
    // head is always text, so make the DML-only runtime query explicit rather than adding any catch-and-create fallback.
    private static NpgsqlParameter NullableTextParameter(string name, string? value) =>
        new(name, NpgsqlDbType.Text) { Value = value is null ? DBNull.Value : value };

    private Task InvokeTagHeadProtocolHookAsync() =>
        TagHeadProtocolHook?.Invoke() ?? Task.CompletedTask;

    private void LogCommittedRepairs(IEnumerable<TagHeadRepair> repairs, TagHeadWriter writer)
    {
        foreach (var repair in repairs)
        {
            // Intentionally after CommitAsync only: a log entry means the append-only violation record and repair are
            // durable together, never a false alarm from an aborted transaction.
            _logger.LogWarning(
                "Postgres tag-head reconciliation violation committed for {ServiceId}/{Tag}: {PreviousHead} -> {ObservedHead} by {Writer}",
                repair.Key.ServiceId,
                repair.Key.Tag,
                repair.PreviousHead ?? "<proven-empty>",
                repair.ObservedHead,
                writer.ToString());
        }
    }

    private enum TagHeadWriter
    {
        TypedBatch,
        SerializedBatch,
        ConditionalClaim,
        ExpectedPositionBatch
    }

    private readonly record struct TagHeadKey(string ServiceId, string Tag);
    private readonly record struct TagHeadRepair(TagHeadKey Key, string? PreviousHead, string ObservedHead);

    // Note: Tag state is not stored in the database
    // Tags table only tracks tag-to-event relationships
    // Tag state should be computed by projectors when needed

    private ResultBox<IEventPayload> DeserializeEventPayload(string eventType, string json)
    {
        try
        {
            var payload = _eventTypes.DeserializeEventPayload(eventType, json);
            if (payload == null)
            {
                return ResultBox.Error<IEventPayload>(
                    new Exception(
                        $"Failed to deserialize event payload of type {eventType}. Make sure the event type is registered."));
            }

            return ResultBox.FromValue(payload);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IEventPayload>(ex);
        }
    }
}
