using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Sqlite;

/// <summary>
///     SQLite implementation of IEventStore.
///     Can be used as a standalone event store or as a local cache for remote stores.
/// </summary>
public class SqliteEventStore : IHotEventStore, IStorageDurabilityDescriptorProvider,
    IConditionalEventStore, IWriteConditionCapabilityProvider, IStreamingTaggedSerializableEventStore,
    ITaggedStreamCapabilityProvider
{
    private const string ConditionalProviderName = "Sqlite";

    /// <summary>SQLite extended result code SQLITE_CONSTRAINT_PRIMARYKEY (1555) — the (ServiceId, Id) PK violation.</summary>
    private const int SqliteConstraintPrimaryKey = 1555;

    private readonly ConditionalAppendCoordinator _conditionalAppend;

    /// <summary>
    ///     Test seam ONLY (never set in production): invoked immediately AFTER the conditional claim durably commits, to
    ///     simulate the response/return being lost (transport error / cancellation) while the write is already durable.
    /// </summary>
    internal Func<Task>? AfterConditionalCommitHook { get; set; }

    /// <summary>
    ///     Test-only tagged-stream reader milestones. The provider tests use these gates around the real
    ///     <see cref="SqliteDataReader.ReadAsync(CancellationToken)" /> call to prove cancellation reaches the native
    ///     reader and that a cancelled callback never triggers a later row read. They are intentionally internal,
    ///     instance-scoped, and unset in production composition.
    /// </summary>
    internal Func<Task>? BeforeTaggedStreamReaderReadHook { get; set; }

    /// <summary>Test-only notification after the real tagged-stream reader returned a row and before it is consumed.</summary>
    internal Func<Task>? AfterTaggedStreamReaderReadHook { get; set; }

    /// <summary>
    ///     SEK-G16 conditional (unique-key) append. This is a NEW path — the unconditional <c>INSERT OR REPLACE</c> write
    ///     paths are untouched. The claim event is written under the deterministic id with a PLAIN <c>INSERT</c>, so the
    ///     existing <c>(ServiceId, Id)</c> primary key is the uniqueness primitive (no schema change): the first writer
    ///     wins; a second writer hits the primary-key constraint and is classified by fingerprint against the stored
    ///     winner. All shared semantics live in <see cref="ConditionalAppendExecution" />.
    /// </summary>
    public Task<ResultBox<ConditionalAppendReceipt>> AppendIfUniqueAsync(
        ConditionalAppendRequest request,
        CancellationToken cancellationToken = default) =>
        _conditionalAppend.AppendIfUniqueAsync(request, cancellationToken);

    /// <inheritdoc />
    public WriteConditionCapabilityDescriptor DescribeWriteConditions() => _conditionalAppend.Descriptor;

    /// <summary>SQLite reads tagged rows directly from an ordered data reader.</summary>
    public TaggedStreamCapabilityDescriptor DescribeTaggedStream() =>
        TaggedStreamCapabilityDescriptor.Native("Sqlite");

    private async Task<ConditionalWriteOutcome> TryWriteConditionalClaimAsync(
        Guid deterministicId,
        SerializableEvent claimEvent,
        CancellationToken cancellationToken)
    {
        var serviceId = CurrentServiceId;
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var committed = false;
            try
            {
                await using var eventCmd = connection.CreateCommand();
                eventCmd.Transaction = (SqliteTransaction)transaction;
                // PLAIN INSERT (not INSERT OR REPLACE): a duplicate (ServiceId, Id) raises the PK constraint.
                eventCmd.CommandText = $"""
                    INSERT INTO dcb_events (ServiceId, Id, SortableUniqueId, EventType, PayloadJson, TagsJson, Timestamp, CausationId, CorrelationId, ExecutedUser)
                    VALUES ({ParamServiceId}, @id, @sortableUniqueId, @eventType, @payloadJson, @tagsJson, @timestamp, @causationId, @correlationId, @executedUser)
                    """;
                eventCmd.Parameters.AddWithValue(ParamServiceId, serviceId);
                eventCmd.Parameters.AddWithValue("@id", deterministicId.ToString());
                eventCmd.Parameters.AddWithValue("@sortableUniqueId", claimEvent.SortableUniqueIdValue);
                eventCmd.Parameters.AddWithValue("@eventType", claimEvent.EventPayloadName);
                eventCmd.Parameters.AddWithValue("@payloadJson", Encoding.UTF8.GetString(claimEvent.Payload));
                eventCmd.Parameters.AddWithValue("@tagsJson", JsonSerializer.Serialize(claimEvent.Tags));
                eventCmd.Parameters.AddWithValue("@timestamp", new SortableUniqueId(claimEvent.SortableUniqueIdValue).GetDateTime().ToString("O"));
                eventCmd.Parameters.AddWithValue("@causationId", (object?)claimEvent.EventMetadata.CausationId ?? DBNull.Value);
                eventCmd.Parameters.AddWithValue("@correlationId", (object?)claimEvent.EventMetadata.CorrelationId ?? DBNull.Value);
                eventCmd.Parameters.AddWithValue("@executedUser", (object?)claimEvent.EventMetadata.ExecutedUser ?? DBNull.Value);

                try
                {
                    await eventCmd.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (SqliteException ex) when (ex.SqliteExtendedErrorCode == SqliteConstraintPrimaryKey)
                {
                    // Only the events-table (ServiceId, Id) PRIMARY KEY violation is the deterministic claim collision.
                    // Any other constraint (a different extended code) rolls back and propagates as a provider failure,
                    // never a winner classification.
                    await transaction.RollbackAsync(cancellationToken);
                    return ConditionalWriteOutcome.Conflict(ex);
                }

                foreach (var tagString in claimEvent.Tags)
                {
                    var tagGroup = tagString.Contains(':') ? tagString.Split(':')[0] : tagString;
                    await using var tagCmd = connection.CreateCommand();
                    tagCmd.Transaction = (SqliteTransaction)transaction;
                    tagCmd.CommandText = $"""
                        INSERT INTO dcb_tags (ServiceId, Tag, TagGroup, SortableUniqueId, EventId, EventType, CreatedAt)
                        VALUES ({ParamServiceId}, @tag, @tagGroup, @sortableUniqueId, @eventId, @eventType, @createdAt)
                        """;
                    tagCmd.Parameters.AddWithValue(ParamServiceId, serviceId);
                    tagCmd.Parameters.AddWithValue("@tag", tagString);
                    tagCmd.Parameters.AddWithValue("@tagGroup", tagGroup);
                    tagCmd.Parameters.AddWithValue("@sortableUniqueId", claimEvent.SortableUniqueIdValue);
                    tagCmd.Parameters.AddWithValue("@eventId", deterministicId.ToString());
                    tagCmd.Parameters.AddWithValue("@eventType", claimEvent.EventPayloadName);
                    tagCmd.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("O"));
                    await tagCmd.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                committed = true;
                // Durably committed: a failure past this point is a LOST RESPONSE, signalled as the post-commit ambiguity
                // marker for the shared orchestrator to resolve authoritatively. (The seam is test-only.)
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
            catch
            {
                // Only roll back a still-open transaction; a post-commit failure (e.g. the response-loss seam) must not
                // attempt to roll back an already-durable commit.
                if (!committed)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                throw;
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<SerializableEvent?> ReadConditionalWinnerAsync(Guid deterministicId, CancellationToken cancellationToken)
    {
        var serviceId = CurrentServiceId;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT Id, SortableUniqueId, EventType, PayloadJson, TagsJson, Timestamp, CausationId, CorrelationId, ExecutedUser
            FROM dcb_events
            WHERE ServiceId = {ParamServiceId} AND Id = @id
            """;
        cmd.Parameters.AddWithValue(ParamServiceId, serviceId);
        cmd.Parameters.AddWithValue("@id", deterministicId.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadSerializableEvent(reader);
    }

    /// <summary>
    ///     Sqlite is durable when it is a file, and volatile when it is <c>:memory:</c> — same type, same class name,
    ///     opposite guarantee. This is the case that proves the descriptor has to be resolved from the live instance
    ///     rather than inferred from the registration: only the instance knows which one it got.
    /// </summary>
    public StorageDurabilityDescriptor DescribeStorage() =>
        IsInMemoryConnection(_connectionString)
            ? new StorageDurabilityDescriptor(StorageDurability.Volatile, "Sqlite (in-memory)")
            : new StorageDurabilityDescriptor(StorageDurability.Durable, "Sqlite");

    private static bool IsInMemoryConnection(string connectionString) =>
        connectionString.Contains(":memory:", StringComparison.OrdinalIgnoreCase)
        || connectionString.Contains("Mode=Memory", StringComparison.OrdinalIgnoreCase);

    private const string SchemaVersion = "1.1";
    private const string SchemaVersionKey = "schemaVersion";
    private const string ParamServiceId = "@serviceId";
    private static readonly HashSet<string> AllowedTableNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "dcb_events",
        "dcb_tags",
        "dcb_meta"
    };
    private static readonly HashSet<string> AllowedColumnNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ServiceId"
    };
    private readonly string _connectionString;
    private readonly string _databasePath;
    private readonly IEventTypes _eventTypes;
    private readonly SqliteEventStoreOptions _options;
    private readonly ILogger<SqliteEventStore>? _logger;
    private readonly IServiceIdProvider _serviceIdProvider;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public SqliteEventStore(
        string databasePath,
        IEventTypes eventTypes,
        SqliteEventStoreOptions? options = null,
        ILogger<SqliteEventStore>? logger = null,
        IServiceIdProvider? serviceIdProvider = null)
    {
        _databasePath = databasePath;
        _connectionString = $"Data Source={databasePath}";
        _eventTypes = eventTypes;
        _options = options ?? new SqliteEventStoreOptions();
        _logger = logger;
        _serviceIdProvider = serviceIdProvider ?? new DefaultServiceIdProvider();
        _conditionalAppend = new ConditionalAppendCoordinator(
            ConditionalProviderName, () => CurrentServiceId, _eventTypes,
            TryWriteConditionalClaimAsync, ReadConditionalWinnerAsync);

        if (_options.AutoCreateDatabase)
        {
            InitializeDatabase();
        }
    }

    /// <summary>
    ///     Gets the database file path
    /// </summary>
    public string DatabasePath => _databasePath;

    private string CurrentServiceId => _serviceIdProvider.GetCurrentServiceId();

    private void InitializeDatabase()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        if (_options.UseWalMode)
        {
            using var walCmd = connection.CreateCommand();
            walCmd.CommandText = "PRAGMA journal_mode=WAL;";
            walCmd.ExecuteNonQuery();
        }
        EnsureSchema(connection);
    }

    private void EnsureSchema(SqliteConnection connection)
    {
        EnsureMetaTable(connection);

        var hasEvents = SqliteSchemaSupport.TableExists(connection, "dcb_events");
        var hasTags = SqliteSchemaSupport.TableExists(connection, "dcb_tags");

        if (!hasEvents || !hasTags)
        {
            CreateSchema(connection);
            SetMetaValue(connection, SchemaVersionKey, SchemaVersion);
            return;
        }

        var hasServiceId = SqliteSchemaSupport.HasColumn(connection, "dcb_events", "ServiceId", AllowedTableNames, AllowedColumnNames)
            && SqliteSchemaSupport.HasColumn(connection, "dcb_tags", "ServiceId", AllowedTableNames, AllowedColumnNames);

        if (!hasServiceId)
        {
            MigrateSchemaToServiceId(connection);
        }
        else
        {
            EnsureIndexes(connection);
        }

        SetMetaValue(connection, SchemaVersionKey, SchemaVersion);
    }

    private static void EnsureMetaTable(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS dcb_meta (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static void CreateSchema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS dcb_events (
                ServiceId TEXT NOT NULL,
                Id TEXT NOT NULL,
                SortableUniqueId TEXT NOT NULL,
                EventType TEXT NOT NULL,
                PayloadJson TEXT NOT NULL,
                TagsJson TEXT,
                Timestamp TEXT NOT NULL,
                CausationId TEXT,
                CorrelationId TEXT,
                ExecutedUser TEXT,
                PRIMARY KEY (ServiceId, Id)
            );
            CREATE INDEX IF NOT EXISTS IX_Events_ServiceId ON dcb_events(ServiceId);
            CREATE INDEX IF NOT EXISTS IX_Events_Service_SortableUniqueId ON dcb_events(ServiceId, SortableUniqueId);
            CREATE INDEX IF NOT EXISTS IX_Events_EventType ON dcb_events(EventType);

            CREATE TABLE IF NOT EXISTS dcb_tags (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ServiceId TEXT NOT NULL,
                Tag TEXT NOT NULL,
                TagGroup TEXT NOT NULL,
                SortableUniqueId TEXT NOT NULL,
                EventId TEXT NOT NULL,
                EventType TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Tags_Service_Tag ON dcb_tags(ServiceId, Tag);
            CREATE INDEX IF NOT EXISTS IX_Tags_Service_TagGroup ON dcb_tags(ServiceId, TagGroup);
            CREATE INDEX IF NOT EXISTS IX_Tags_Service_Tag_SortableUniqueId ON dcb_tags(ServiceId, Tag, SortableUniqueId);
            """;
        cmd.ExecuteNonQuery();
    }

    private static void EnsureIndexes(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var cmd = connection.CreateCommand();
        if (transaction != null)
        {
            cmd.Transaction = transaction;
        }
        cmd.CommandText = """
            CREATE INDEX IF NOT EXISTS IX_Events_ServiceId ON dcb_events(ServiceId);
            CREATE INDEX IF NOT EXISTS IX_Events_Service_SortableUniqueId ON dcb_events(ServiceId, SortableUniqueId);
            CREATE INDEX IF NOT EXISTS IX_Events_EventType ON dcb_events(EventType);

            CREATE INDEX IF NOT EXISTS IX_Tags_Service_Tag ON dcb_tags(ServiceId, Tag);
            CREATE INDEX IF NOT EXISTS IX_Tags_Service_TagGroup ON dcb_tags(ServiceId, TagGroup);
            CREATE INDEX IF NOT EXISTS IX_Tags_Service_Tag_SortableUniqueId ON dcb_tags(ServiceId, Tag, SortableUniqueId);
            """;
        cmd.ExecuteNonQuery();
    }

    private static void MigrateSchemaToServiceId(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        try
        {
            using var createCmd = connection.CreateCommand();
            createCmd.Transaction = transaction;
            createCmd.CommandText = """
                CREATE TABLE dcb_events_new (
                    ServiceId TEXT NOT NULL,
                    Id TEXT NOT NULL,
                    SortableUniqueId TEXT NOT NULL,
                    EventType TEXT NOT NULL,
                    PayloadJson TEXT NOT NULL,
                    TagsJson TEXT,
                    Timestamp TEXT NOT NULL,
                    CausationId TEXT,
                    CorrelationId TEXT,
                    ExecutedUser TEXT,
                    PRIMARY KEY (ServiceId, Id)
                );
                CREATE TABLE dcb_tags_new (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ServiceId TEXT NOT NULL,
                    Tag TEXT NOT NULL,
                    TagGroup TEXT NOT NULL,
                    SortableUniqueId TEXT NOT NULL,
                    EventId TEXT NOT NULL,
                    EventType TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL
                );
                """;
            createCmd.ExecuteNonQuery();

            using var copyEventsCmd = connection.CreateCommand();
            copyEventsCmd.Transaction = transaction;
            copyEventsCmd.CommandText = $"""
                INSERT INTO dcb_events_new
                (ServiceId, Id, SortableUniqueId, EventType, PayloadJson, TagsJson, Timestamp, CausationId, CorrelationId, ExecutedUser)
                SELECT {ParamServiceId}, Id, SortableUniqueId, EventType, PayloadJson, TagsJson, Timestamp, CausationId, CorrelationId, ExecutedUser
                FROM dcb_events;
                """;
            copyEventsCmd.Parameters.AddWithValue(ParamServiceId, DefaultServiceIdProvider.DefaultServiceId);
            copyEventsCmd.ExecuteNonQuery();

            using var copyTagsCmd = connection.CreateCommand();
            copyTagsCmd.Transaction = transaction;
            copyTagsCmd.CommandText = $"""
                INSERT INTO dcb_tags_new
                (ServiceId, Tag, TagGroup, SortableUniqueId, EventId, EventType, CreatedAt)
                SELECT {ParamServiceId}, Tag, TagGroup, SortableUniqueId, EventId, EventType, CreatedAt
                FROM dcb_tags;
                """;
            copyTagsCmd.Parameters.AddWithValue(ParamServiceId, DefaultServiceIdProvider.DefaultServiceId);
            copyTagsCmd.ExecuteNonQuery();

            using var dropCmd = connection.CreateCommand();
            dropCmd.Transaction = transaction;
            dropCmd.CommandText = """
                DROP TABLE dcb_events;
                DROP TABLE dcb_tags;
                """;
            dropCmd.ExecuteNonQuery();

            using var renameCmd = connection.CreateCommand();
            renameCmd.Transaction = transaction;
            renameCmd.CommandText = """
                ALTER TABLE dcb_events_new RENAME TO dcb_events;
                ALTER TABLE dcb_tags_new RENAME TO dcb_tags;
                """;
            renameCmd.ExecuteNonQuery();

            EnsureIndexes(connection, transaction);

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<ResultBox<IEnumerable<Event>>> ReadAllEventsAsync(SortableUniqueId? since = null, int? maxCount = null)
    {
        try
        {
            var events = new List<Event>();
            var serviceId = CurrentServiceId;

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            if (since != null)
            {
                cmd.CommandText = $"""
                    SELECT Id, SortableUniqueId, EventType, PayloadJson, TagsJson, Timestamp, CausationId, CorrelationId, ExecutedUser
                    FROM dcb_events
                    WHERE ServiceId = {ParamServiceId} AND SortableUniqueId > @since
                    ORDER BY SortableUniqueId
                    """;
                cmd.Parameters.AddWithValue("@since", since.Value);
            }
            else
            {
                cmd.CommandText = $"""
                    SELECT Id, SortableUniqueId, EventType, PayloadJson, TagsJson, Timestamp, CausationId, CorrelationId, ExecutedUser
                    FROM dcb_events
                    WHERE ServiceId = {ParamServiceId}
                    ORDER BY SortableUniqueId
                    """;
            }
            if (maxCount.HasValue)
            {
                cmd.CommandText += "\nLIMIT @maxCount";
                cmd.Parameters.AddWithValue("@maxCount", maxCount.Value);
            }
            cmd.Parameters.AddWithValue(ParamServiceId, serviceId);

            await using var reader = await cmd.ExecuteReaderAsync();
            var eventsRead = 0;

            while (await reader.ReadAsync())
            {
                var evt = ReadEvent(reader);
                if (evt != null)
                {
                    events.Add(evt);
                    eventsRead++;

                    if (_options.ReadProgressCallback != null && eventsRead % 1000 == 0)
                    {
                        _options.ReadProgressCallback(eventsRead, 0);
                    }
                }
            }

            return ResultBox.FromValue<IEnumerable<Event>>(events);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error reading all events from SQLite");
            return ResultBox.Error<IEnumerable<Event>>(ex);
        }
    }

    public async Task<ResultBox<IEnumerable<Event>>> ReadEventsByTagAsync(ITag tag, SortableUniqueId? since = null)
    {
        try
        {
            var events = new List<Event>();
            var tagString = tag.GetTag();
            var serviceId = CurrentServiceId;

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            if (since != null)
            {
                cmd.CommandText = $"""
                    SELECT DISTINCT e.Id, e.SortableUniqueId, e.EventType, e.PayloadJson, e.TagsJson, e.Timestamp, e.CausationId, e.CorrelationId, e.ExecutedUser
                    FROM dcb_events e
                    INNER JOIN dcb_tags t ON e.Id = t.EventId
                    WHERE e.ServiceId = {ParamServiceId} AND t.ServiceId = {ParamServiceId} AND t.Tag = @tag AND e.SortableUniqueId > @since
                    ORDER BY e.SortableUniqueId
                    """;
                cmd.Parameters.AddWithValue("@tag", tagString);
                cmd.Parameters.AddWithValue("@since", since.Value);
            }
            else
            {
                cmd.CommandText = $"""
                    SELECT DISTINCT e.Id, e.SortableUniqueId, e.EventType, e.PayloadJson, e.TagsJson, e.Timestamp, e.CausationId, e.CorrelationId, e.ExecutedUser
                    FROM dcb_events e
                    INNER JOIN dcb_tags t ON e.Id = t.EventId
                    WHERE e.ServiceId = {ParamServiceId} AND t.ServiceId = {ParamServiceId} AND t.Tag = @tag
                    ORDER BY e.SortableUniqueId
                    """;
                cmd.Parameters.AddWithValue("@tag", tagString);
            }
            cmd.Parameters.AddWithValue(ParamServiceId, serviceId);

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var evt = ReadEvent(reader);
                if (evt != null)
                {
                    events.Add(evt);
                }
            }

            return ResultBox.FromValue<IEnumerable<Event>>(events);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error reading events by tag from SQLite: {Tag}", tag.GetTag());
            return ResultBox.Error<IEnumerable<Event>>(ex);
        }
    }

    public async Task<ResultBox<Event>> ReadEventAsync(Guid eventId)
    {
        try
        {
            var serviceId = CurrentServiceId;
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"""
                SELECT Id, SortableUniqueId, EventType, PayloadJson, TagsJson, Timestamp, CausationId, CorrelationId, ExecutedUser
                FROM dcb_events
                WHERE ServiceId = {ParamServiceId} AND Id = @id
                """;
            cmd.Parameters.AddWithValue("@id", eventId.ToString());
            cmd.Parameters.AddWithValue(ParamServiceId, serviceId);

            await using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                var evt = ReadEvent(reader);
                if (evt != null)
                {
                    return ResultBox.FromValue(evt);
                }
            }

            return ResultBox.Error<Event>(new KeyNotFoundException($"Event not found: {eventId}"));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error reading event from SQLite: {EventId}", eventId);
            return ResultBox.Error<Event>(ex);
        }
    }

    public async Task<ResultBox<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteEventsAsync(
        IEnumerable<Event> events)
    {
        var eventList = events.ToList();
        if (eventList.Count == 0)
        {
            return ResultBox.FromValue<(IReadOnlyList<Event>, IReadOnlyList<TagWriteResult>)>(
                (Array.Empty<Event>(), Array.Empty<TagWriteResult>()));
        }

        var serviceId = CurrentServiceId;

        // Serialize all write operations to ensure consistency
        await _writeLock.WaitAsync();
        try
        {
            var tagWrites = new List<TagWriteResult>();

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                foreach (var evt in eventList)
                {
                    // Insert event
                    await using var eventCmd = connection.CreateCommand();
                    eventCmd.Transaction = (SqliteTransaction)transaction;
                    eventCmd.CommandText = $"""
                        INSERT OR REPLACE INTO dcb_events (ServiceId, Id, SortableUniqueId, EventType, PayloadJson, TagsJson, Timestamp, CausationId, CorrelationId, ExecutedUser)
                        VALUES ({ParamServiceId}, @id, @sortableUniqueId, @eventType, @payloadJson, @tagsJson, @timestamp, @causationId, @correlationId, @executedUser)
                        """;

                    var payloadJson = _eventTypes.SerializeEventPayload(evt.Payload);
                    var tagsJson = JsonSerializer.Serialize(evt.Tags);

                    eventCmd.Parameters.AddWithValue(ParamServiceId, serviceId);
                    eventCmd.Parameters.AddWithValue("@id", evt.Id.ToString());
                    eventCmd.Parameters.AddWithValue("@sortableUniqueId", evt.SortableUniqueIdValue);
                    eventCmd.Parameters.AddWithValue("@eventType", evt.EventType);
                    eventCmd.Parameters.AddWithValue("@payloadJson", payloadJson);
                    eventCmd.Parameters.AddWithValue("@tagsJson", tagsJson);
                    eventCmd.Parameters.AddWithValue("@timestamp", new SortableUniqueId(evt.SortableUniqueIdValue).GetDateTime().ToString("O"));
                    eventCmd.Parameters.AddWithValue("@causationId", (object?)evt.EventMetadata.CausationId ?? DBNull.Value);
                    eventCmd.Parameters.AddWithValue("@correlationId", (object?)evt.EventMetadata.CorrelationId ?? DBNull.Value);
                    eventCmd.Parameters.AddWithValue("@executedUser", (object?)evt.EventMetadata.ExecutedUser ?? DBNull.Value);

                    await eventCmd.ExecuteNonQueryAsync();

                    // Insert tags
                    foreach (var tagString in evt.Tags)
                    {
                        var tagGroup = tagString.Contains(':') ? tagString.Split(':')[0] : tagString;

                        await using var tagCmd = connection.CreateCommand();
                        tagCmd.Transaction = (SqliteTransaction)transaction;
                        tagCmd.CommandText = $"""
                            INSERT INTO dcb_tags (ServiceId, Tag, TagGroup, SortableUniqueId, EventId, EventType, CreatedAt)
                            VALUES ({ParamServiceId}, @tag, @tagGroup, @sortableUniqueId, @eventId, @eventType, @createdAt)
                            """;
                        tagCmd.Parameters.AddWithValue(ParamServiceId, serviceId);
                        tagCmd.Parameters.AddWithValue("@tag", tagString);
                        tagCmd.Parameters.AddWithValue("@tagGroup", tagGroup);
                        tagCmd.Parameters.AddWithValue("@sortableUniqueId", evt.SortableUniqueIdValue);
                        tagCmd.Parameters.AddWithValue("@eventId", evt.Id.ToString());
                        tagCmd.Parameters.AddWithValue("@eventType", evt.EventType);
                        tagCmd.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("O"));

                        await tagCmd.ExecuteNonQueryAsync();

                        tagWrites.Add(new TagWriteResult(tagString, 1, DateTimeOffset.UtcNow));
                    }
                }

                await transaction.CommitAsync();

                return ResultBox.FromValue<(IReadOnlyList<Event>, IReadOnlyList<TagWriteResult>)>(
                    (eventList, tagWrites));
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error writing events to SQLite");
            return ResultBox.Error<(IReadOnlyList<Event>, IReadOnlyList<TagWriteResult>)>(ex);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag)
    {
        try
        {
            var streams = new List<TagStream>();
            var tagString = tag.GetTag();
            var serviceId = CurrentServiceId;

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"""
                SELECT EventId, SortableUniqueId, EventType
                FROM dcb_tags
                WHERE ServiceId = {ParamServiceId} AND Tag = @tag
                ORDER BY SortableUniqueId
                """;
            cmd.Parameters.AddWithValue("@tag", tagString);
            cmd.Parameters.AddWithValue(ParamServiceId, serviceId);

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var eventId = Guid.Parse(reader.GetString(0));
                var sortableUniqueId = reader.GetString(1);

                streams.Add(new TagStream(tagString, eventId, sortableUniqueId));
            }

            return ResultBox.FromValue<IEnumerable<TagStream>>(streams);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error reading tags from SQLite: {Tag}", tag.GetTag());
            return ResultBox.Error<IEnumerable<TagStream>>(ex);
        }
    }

    public async Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag)
    {
        try
        {
            var tagString = tag.GetTag();
            var tagGroup = tag.GetTagGroup();
            var tagContent = tag.GetTagContent();
            var serviceId = CurrentServiceId;

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // Get count for version
            await using var countCmd = connection.CreateCommand();
            countCmd.CommandText = $"SELECT COUNT(*) FROM dcb_tags WHERE ServiceId = {ParamServiceId} AND Tag = @tag";
            countCmd.Parameters.AddWithValue("@tag", tagString);
            countCmd.Parameters.AddWithValue(ParamServiceId, serviceId);
            var version = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"""
                SELECT EventId, SortableUniqueId, EventType
                FROM dcb_tags
                WHERE ServiceId = {ParamServiceId} AND Tag = @tag
                ORDER BY SortableUniqueId DESC
                LIMIT 1
                """;
            cmd.Parameters.AddWithValue("@tag", tagString);
            cmd.Parameters.AddWithValue(ParamServiceId, serviceId);

            await using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                var sortableUniqueId = reader.GetString(1);
                return ResultBox.FromValue(new TagState(
                    new EmptyTagStatePayload(),
                    version,
                    sortableUniqueId,
                    tagGroup,
                    tagContent,
                    "SqliteProjector",
                    string.Empty));
            }

            // No events found - return empty state
            return ResultBox.FromValue(new TagState(
                new EmptyTagStatePayload(),
                0,
                string.Empty,
                tagGroup,
                tagContent,
                "SqliteProjector",
                string.Empty));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error getting latest tag from SQLite: {Tag}", tag.GetTag());
            return ResultBox.Error<TagState>(ex);
        }
    }

    public async Task<ResultBox<bool>> TagExistsAsync(ITag tag)
    {
        try
        {
            var tagString = tag.GetTag();
            var serviceId = CurrentServiceId;

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM dcb_tags WHERE ServiceId = {ParamServiceId} AND Tag = @tag LIMIT 1";
            cmd.Parameters.AddWithValue("@tag", tagString);
            cmd.Parameters.AddWithValue(ParamServiceId, serviceId);

            var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            return ResultBox.FromValue(count > 0);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error checking tag existence in SQLite: {Tag}", tag.GetTag());
            return ResultBox.Error<bool>(ex);
        }
    }

    public async Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null)
    {
        try
        {
            var serviceId = CurrentServiceId;
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            if (since != null)
            {
                cmd.CommandText = $"SELECT COUNT(*) FROM dcb_events WHERE ServiceId = {ParamServiceId} AND SortableUniqueId > @since";
                cmd.Parameters.AddWithValue("@since", since.Value);
            }
            else
            {
                cmd.CommandText = $"SELECT COUNT(*) FROM dcb_events WHERE ServiceId = {ParamServiceId}";
            }
            cmd.Parameters.AddWithValue(ParamServiceId, serviceId);

            var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            return ResultBox.FromValue(count);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error getting event count from SQLite");
            return ResultBox.Error<long>(ex);
        }
    }

    public async Task<ResultBox<string>> GetLatestSortableUniqueIdAsync()
    {
        try
        {
            var serviceId = CurrentServiceId;
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT MAX(SortableUniqueId) FROM dcb_events WHERE ServiceId = {ParamServiceId}";
            cmd.Parameters.AddWithValue(ParamServiceId, serviceId);

            var result = await cmd.ExecuteScalarAsync();
            var latest = result as string ?? string.Empty;
            return ResultBox.FromValue(latest);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error getting latest SortableUniqueId from SQLite");
            return ResultBox.Error<string>(ex);
        }
    }

    public async Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null)
    {
        try
        {
            var tags = new List<TagInfo>();
            var serviceId = CurrentServiceId;

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            if (!string.IsNullOrEmpty(tagGroup))
            {
                cmd.CommandText = $"""
                    SELECT Tag, TagGroup, COUNT(*) as EventCount,
                           MIN(SortableUniqueId) as FirstId, MAX(SortableUniqueId) as LastId
                    FROM dcb_tags
                    WHERE ServiceId = {ParamServiceId} AND TagGroup = @tagGroup
                    GROUP BY Tag, TagGroup
                    ORDER BY Tag
                    """;
                cmd.Parameters.AddWithValue("@tagGroup", tagGroup);
            }
            else
            {
                cmd.CommandText = $"""
                    SELECT Tag, TagGroup, COUNT(*) as EventCount,
                           MIN(SortableUniqueId) as FirstId, MAX(SortableUniqueId) as LastId
                    FROM dcb_tags
                    WHERE ServiceId = {ParamServiceId}
                    GROUP BY Tag, TagGroup
                    ORDER BY TagGroup, Tag
                    """;
            }
            cmd.Parameters.AddWithValue(ParamServiceId, serviceId);

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var tag = reader.GetString(0);
                var group = reader.GetString(1);
                var eventCount = reader.GetInt32(2);
                var firstId = reader.IsDBNull(3) ? null : reader.GetString(3);
                var lastId = reader.IsDBNull(4) ? null : reader.GetString(4);

                DateTime? firstEventAt = null;
                DateTime? lastEventAt = null;

                if (firstId != null)
                {
                    firstEventAt = new SortableUniqueId(firstId).GetDateTime();
                }
                if (lastId != null)
                {
                    lastEventAt = new SortableUniqueId(lastId).GetDateTime();
                }

                tags.Add(new TagInfo(tag, group, eventCount, firstId, lastId, firstEventAt, lastEventAt));
            }

            return ResultBox.FromValue<IEnumerable<TagInfo>>(tags);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error getting all tags from SQLite");
            return ResultBox.Error<IEnumerable<TagInfo>>(ex);
        }
    }

    // Cache-specific methods

    /// <summary>
    ///     Clear all data from the cache
    /// </summary>
    public async Task ClearAsync()
    {
        var serviceId = CurrentServiceId;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            DELETE FROM dcb_events WHERE ServiceId = {ParamServiceId};
            DELETE FROM dcb_tags WHERE ServiceId = {ParamServiceId};
            """;
        cmd.Parameters.AddWithValue(ParamServiceId, serviceId);
        await cmd.ExecuteNonQueryAsync();

        await using var metaCmd = connection.CreateCommand();
        metaCmd.CommandText = "DELETE FROM dcb_meta WHERE Key LIKE @keyPrefix";
        metaCmd.Parameters.AddWithValue("@keyPrefix", $"{BuildMetaKeyPrefix(serviceId)}%");
        await metaCmd.ExecuteNonQueryAsync();

        if (string.Equals(serviceId, DefaultServiceIdProvider.DefaultServiceId, StringComparison.Ordinal))
        {
            await using var legacyMetaCmd = connection.CreateCommand();
            legacyMetaCmd.CommandText = "DELETE FROM dcb_meta WHERE Key NOT LIKE '%:%'";
            await legacyMetaCmd.ExecuteNonQueryAsync();
        }

        _logger?.LogInformation("SQLite cache cleared");
    }

    /// <summary>
    ///     Get cache metadata
    /// </summary>
    public async Task<CacheMetadata?> GetMetadataAsync()
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var remoteEndpoint = GetMetaValue(connection, "remoteEndpoint");
            if (string.IsNullOrEmpty(remoteEndpoint))
            {
                return null;
            }

            return new CacheMetadata
            {
                RemoteEndpoint = remoteEndpoint,
                DatabaseName = GetMetaValue(connection, "databaseName") ?? "",
                SchemaVersion = GetMetaValue(connection, SchemaVersionKey) ?? "",
                TotalCountAtFetch = long.TryParse(GetMetaValue(connection, "totalCountAtFetch"), out var count) ? count : 0,
                LastCachedSortableUniqueId = GetMetaValue(connection, "lastCachedSortableUniqueId"),
                LastSafeWindowUtc = DateTime.TryParse(GetMetaValue(connection, "lastSafeWindowUtc"), out var dt) ? dt : null,
                CreatedUtc = DateTime.TryParse(GetMetaValue(connection, "createdUtc"), out var created) ? created : DateTime.UtcNow,
                UpdatedUtc = DateTime.TryParse(GetMetaValue(connection, "updatedUtc"), out var updated) ? updated : DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error getting cache metadata");
            return null;
        }
    }

    /// <summary>
    ///     Set cache metadata
    /// </summary>
    public async Task SetMetadataAsync(CacheMetadata metadata)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        SetMetaValue(connection, "remoteEndpoint", metadata.RemoteEndpoint);
        SetMetaValue(connection, "databaseName", metadata.DatabaseName);
        SetMetaValue(connection, SchemaVersionKey, metadata.SchemaVersion);
        SetMetaValue(connection, "totalCountAtFetch", metadata.TotalCountAtFetch.ToString());
        SetMetaValue(connection, "lastCachedSortableUniqueId", metadata.LastCachedSortableUniqueId ?? "");
        SetMetaValue(connection, "lastSafeWindowUtc", metadata.LastSafeWindowUtc?.ToString("O") ?? "");
        SetMetaValue(connection, "createdUtc", metadata.CreatedUtc.ToString("O"));
        SetMetaValue(connection, "updatedUtc", metadata.UpdatedUtc.ToString("O"));
    }

    /// <summary>
    ///     Get the last cached SortableUniqueId
    /// </summary>
    public async Task<string?> GetLastCachedIdAsync()
    {
        var serviceId = CurrentServiceId;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT MAX(SortableUniqueId) FROM dcb_events WHERE ServiceId = {ParamServiceId}";
        cmd.Parameters.AddWithValue(ParamServiceId, serviceId);
        var result = await cmd.ExecuteScalarAsync();
        return result as string;
    }

    private Event? ReadEvent(SqliteDataReader reader)
    {
        try
        {
            var id = Guid.Parse(reader.GetString(0));
            var sortableUniqueId = reader.GetString(1);
            var eventType = reader.GetString(2);
            var payloadJson = reader.GetString(3);
            var tagsJson = reader.IsDBNull(4) ? "[]" : reader.GetString(4);
            var causationId = reader.IsDBNull(6) ? null : reader.GetString(6);
            var correlationId = reader.IsDBNull(7) ? null : reader.GetString(7);
            var executedUser = reader.IsDBNull(8) ? null : reader.GetString(8);

            var payload = _eventTypes.DeserializeEventPayload(eventType, payloadJson);
            if (payload == null)
            {
                _logger?.LogWarning("Failed to deserialize event payload: {EventType}", eventType);
                return null;
            }

            var tags = JsonSerializer.Deserialize<List<string>>(tagsJson) ?? [];
            var metadata = new EventMetadata(causationId ?? "", correlationId ?? "", executedUser ?? "");

            return new Event(payload, sortableUniqueId, eventType, id, metadata, tags);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error reading event from SQLite reader");
            return null;
        }
    }

    public async Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null)
    {
        try
        {
            var events = new List<SerializableEvent>();
            var serviceId = CurrentServiceId;

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            if (since != null)
            {
                cmd.CommandText = $"""
                    SELECT Id, SortableUniqueId, EventType, PayloadJson, TagsJson, Timestamp, CausationId, CorrelationId, ExecutedUser
                    FROM dcb_events
                    WHERE ServiceId = {ParamServiceId} AND SortableUniqueId > @since
                    ORDER BY SortableUniqueId
                    """;
                cmd.Parameters.AddWithValue("@since", since.Value);
            }
            else
            {
                cmd.CommandText = $"""
                    SELECT Id, SortableUniqueId, EventType, PayloadJson, TagsJson, Timestamp, CausationId, CorrelationId, ExecutedUser
                    FROM dcb_events
                    WHERE ServiceId = {ParamServiceId}
                    ORDER BY SortableUniqueId
                    """;
            }
            cmd.Parameters.AddWithValue(ParamServiceId, serviceId);

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var se = ReadSerializableEvent(reader);
                if (se != null)
                {
                    events.Add(se);
                }
            }

            return ResultBox.FromValue<IEnumerable<SerializableEvent>>(events);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error reading all serializable events from SQLite");
            return ResultBox.Error<IEnumerable<SerializableEvent>>(ex);
        }
    }

    public async Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
        SortableUniqueId? since,
        int? maxCount)
    {
        try
        {
            var events = new List<SerializableEvent>();
            var serviceId = CurrentServiceId;

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            if (since != null)
            {
                cmd.CommandText = maxCount.HasValue
                    ? $"""
                        SELECT Id, SortableUniqueId, EventType, PayloadJson, TagsJson, Timestamp, CausationId, CorrelationId, ExecutedUser
                        FROM dcb_events
                        WHERE ServiceId = {ParamServiceId} AND SortableUniqueId > @since
                        ORDER BY SortableUniqueId
                        LIMIT @maxCount
                        """
                    : $"""
                        SELECT Id, SortableUniqueId, EventType, PayloadJson, TagsJson, Timestamp, CausationId, CorrelationId, ExecutedUser
                        FROM dcb_events
                        WHERE ServiceId = {ParamServiceId} AND SortableUniqueId > @since
                        ORDER BY SortableUniqueId
                        """;
                cmd.Parameters.AddWithValue("@since", since.Value);
            }
            else
            {
                cmd.CommandText = maxCount.HasValue
                    ? $"""
                        SELECT Id, SortableUniqueId, EventType, PayloadJson, TagsJson, Timestamp, CausationId, CorrelationId, ExecutedUser
                        FROM dcb_events
                        WHERE ServiceId = {ParamServiceId}
                        ORDER BY SortableUniqueId
                        LIMIT @maxCount
                        """
                    : $"""
                        SELECT Id, SortableUniqueId, EventType, PayloadJson, TagsJson, Timestamp, CausationId, CorrelationId, ExecutedUser
                        FROM dcb_events
                        WHERE ServiceId = {ParamServiceId}
                        ORDER BY SortableUniqueId
                        """;
            }
            cmd.Parameters.AddWithValue(ParamServiceId, serviceId);
            if (maxCount.HasValue)
            {
                cmd.Parameters.AddWithValue("@maxCount", maxCount.Value);
            }

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var se = ReadSerializableEvent(reader);
                if (se != null)
                {
                    events.Add(se);
                }
            }

            return ResultBox.FromValue<IEnumerable<SerializableEvent>>(events);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error reading all serializable events from SQLite with maxCount");
            return ResultBox.Error<IEnumerable<SerializableEvent>>(ex);
        }
    }

    public async Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(ITag tag, SortableUniqueId? since = null)
    {
        try
        {
            var events = new List<SerializableEvent>();
            var tagString = tag.GetTag();
            var serviceId = CurrentServiceId;

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            if (since != null)
            {
                cmd.CommandText = $"""
                    SELECT DISTINCT e.Id, e.SortableUniqueId, e.EventType, e.PayloadJson, e.TagsJson, e.Timestamp, e.CausationId, e.CorrelationId, e.ExecutedUser
                    FROM dcb_events e
                    INNER JOIN dcb_tags t ON e.Id = t.EventId
                    WHERE e.ServiceId = {ParamServiceId} AND t.ServiceId = {ParamServiceId} AND t.Tag = @tag AND e.SortableUniqueId > @since
                    ORDER BY e.SortableUniqueId
                    """;
                cmd.Parameters.AddWithValue("@tag", tagString);
                cmd.Parameters.AddWithValue("@since", since.Value);
            }
            else
            {
                cmd.CommandText = $"""
                    SELECT DISTINCT e.Id, e.SortableUniqueId, e.EventType, e.PayloadJson, e.TagsJson, e.Timestamp, e.CausationId, e.CorrelationId, e.ExecutedUser
                    FROM dcb_events e
                    INNER JOIN dcb_tags t ON e.Id = t.EventId
                    WHERE e.ServiceId = {ParamServiceId} AND t.ServiceId = {ParamServiceId} AND t.Tag = @tag
                    ORDER BY e.SortableUniqueId
                    """;
                cmd.Parameters.AddWithValue("@tag", tagString);
            }
            cmd.Parameters.AddWithValue(ParamServiceId, serviceId);

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var se = ReadSerializableEvent(reader);
                if (se != null)
                {
                    events.Add(se);
                }
            }

            return ResultBox.FromValue<IEnumerable<SerializableEvent>>(events);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error reading serializable events by tag from SQLite: {Tag}", tag.GetTag());
            return ResultBox.Error<IEnumerable<SerializableEvent>>(ex);
        }
    }

    /// <summary>
    ///     Streams a tag from the SQLite reader. Bounds stay in SQL; cancellation reaches opening the connection,
    ///     executing the command, and reading every row.
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
            var tagString = tag.GetTag();
            var serviceId = CurrentServiceId;

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var cmd = connection.CreateCommand();

            cmd.CommandText = """
                SELECT DISTINCT e.Id, e.SortableUniqueId, e.EventType, e.PayloadJson, e.TagsJson, e.Timestamp, e.CausationId, e.CorrelationId, e.ExecutedUser
                FROM dcb_events e
                INNER JOIN dcb_tags t ON e.Id = t.EventId
                WHERE e.ServiceId = @serviceId
                    AND t.ServiceId = @serviceId
                    AND t.Tag = @tag
                    AND (@since IS NULL OR e.SortableUniqueId > @since)
                    AND (@until IS NULL OR e.SortableUniqueId <= @until)
                ORDER BY e.SortableUniqueId
                """;
            cmd.Parameters.AddWithValue(ParamServiceId, serviceId);
            cmd.Parameters.AddWithValue("@tag", tagString);
            cmd.Parameters.AddWithValue("@since", (object?)since?.Value ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@until", (object?)until?.Value ?? DBNull.Value);

            var count = 0;
            string? lastSortableUniqueId = null;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (true)
            {
                if (BeforeTaggedStreamReaderReadHook is not null)
                {
                    await BeforeTaggedStreamReaderReadHook();
                }

                if (!await reader.ReadAsync(cancellationToken))
                {
                    break;
                }

                if (AfterTaggedStreamReaderReadHook is not null)
                {
                    await AfterTaggedStreamReaderReadHook();
                }

                cancellationToken.ThrowIfCancellationRequested();
                var serializableEvent = ReadSerializableEvent(reader);
                if (serializableEvent is null)
                {
                    continue;
                }

                await onEvent(serializableEvent);
                cancellationToken.ThrowIfCancellationRequested();
                count++;
                lastSortableUniqueId = serializableEvent.SortableUniqueIdValue;
            }

            return ResultBox.FromValue(new SerializableEventStreamReadResult(count, lastSortableUniqueId));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error streaming serializable events by tag from SQLite: {Tag}", tag.GetTag());
            return ResultBox.Error<SerializableEventStreamReadResult>(ex);
        }
    }

    public async Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId)
    {
        var typedResult = await ReadEventAsync(eventId);
        if (!typedResult.IsSuccess)
        {
            return ResultBox.Error<SerializableEvent>(typedResult.GetException());
        }

        return ResultBox.FromValue(typedResult.GetValue().ToSerializableEvent(_eventTypes));
    }

    public async Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteSerializableEventsAsync(
        IEnumerable<SerializableEvent> events)
    {
        var eventList = events.ToList();
        if (eventList.Count == 0)
        {
            return ResultBox.FromValue<(IReadOnlyList<SerializableEvent>, IReadOnlyList<TagWriteResult>)>(
                (Array.Empty<SerializableEvent>(), Array.Empty<TagWriteResult>()));
        }

        var serviceId = CurrentServiceId;

        await _writeLock.WaitAsync();
        try
        {
            var tagWrites = new List<TagWriteResult>();

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                foreach (var se in eventList)
                {
                    await using var eventCmd = connection.CreateCommand();
                    eventCmd.Transaction = (SqliteTransaction)transaction;
                    eventCmd.CommandText = $"""
                        INSERT OR REPLACE INTO dcb_events (ServiceId, Id, SortableUniqueId, EventType, PayloadJson, TagsJson, Timestamp, CausationId, CorrelationId, ExecutedUser)
                        VALUES ({ParamServiceId}, @id, @sortableUniqueId, @eventType, @payloadJson, @tagsJson, @timestamp, @causationId, @correlationId, @executedUser)
                        """;

                    var payloadJson = Encoding.UTF8.GetString(se.Payload);
                    var tagsJson = JsonSerializer.Serialize(se.Tags);

                    eventCmd.Parameters.AddWithValue(ParamServiceId, serviceId);
                    eventCmd.Parameters.AddWithValue("@id", se.Id.ToString());
                    eventCmd.Parameters.AddWithValue("@sortableUniqueId", se.SortableUniqueIdValue);
                    eventCmd.Parameters.AddWithValue("@eventType", se.EventPayloadName);
                    eventCmd.Parameters.AddWithValue("@payloadJson", payloadJson);
                    eventCmd.Parameters.AddWithValue("@tagsJson", tagsJson);
                    eventCmd.Parameters.AddWithValue("@timestamp", new SortableUniqueId(se.SortableUniqueIdValue).GetDateTime().ToString("O"));
                    eventCmd.Parameters.AddWithValue("@causationId", (object?)se.EventMetadata.CausationId ?? DBNull.Value);
                    eventCmd.Parameters.AddWithValue("@correlationId", (object?)se.EventMetadata.CorrelationId ?? DBNull.Value);
                    eventCmd.Parameters.AddWithValue("@executedUser", (object?)se.EventMetadata.ExecutedUser ?? DBNull.Value);

                    await eventCmd.ExecuteNonQueryAsync();

                    foreach (var tagString in se.Tags)
                    {
                        var tagGroup = tagString.Contains(':') ? tagString.Split(':')[0] : tagString;

                        await using var tagCmd = connection.CreateCommand();
                        tagCmd.Transaction = (SqliteTransaction)transaction;
                        tagCmd.CommandText = $"""
                            INSERT INTO dcb_tags (ServiceId, Tag, TagGroup, SortableUniqueId, EventId, EventType, CreatedAt)
                            VALUES ({ParamServiceId}, @tag, @tagGroup, @sortableUniqueId, @eventId, @eventType, @createdAt)
                            """;
                        tagCmd.Parameters.AddWithValue(ParamServiceId, serviceId);
                        tagCmd.Parameters.AddWithValue("@tag", tagString);
                        tagCmd.Parameters.AddWithValue("@tagGroup", tagGroup);
                        tagCmd.Parameters.AddWithValue("@sortableUniqueId", se.SortableUniqueIdValue);
                        tagCmd.Parameters.AddWithValue("@eventId", se.Id.ToString());
                        tagCmd.Parameters.AddWithValue("@eventType", se.EventPayloadName);
                        tagCmd.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("O"));

                        await tagCmd.ExecuteNonQueryAsync();

                        tagWrites.Add(new TagWriteResult(tagString, 1, DateTimeOffset.UtcNow));
                    }
                }

                await transaction.CommitAsync();

                return ResultBox.FromValue<(IReadOnlyList<SerializableEvent>, IReadOnlyList<TagWriteResult>)>(
                    (eventList, tagWrites));
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error writing serializable events to SQLite");
            return ResultBox.Error<(IReadOnlyList<SerializableEvent>, IReadOnlyList<TagWriteResult>)>(ex);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private SerializableEvent? ReadSerializableEvent(SqliteDataReader reader)
    {
        try
        {
            var id = Guid.Parse(reader.GetString(0));
            var sortableUniqueId = reader.GetString(1);
            var eventType = reader.GetString(2);
            var payloadJson = reader.GetString(3);
            var tagsJson = reader.IsDBNull(4) ? "[]" : reader.GetString(4);
            var causationId = reader.IsDBNull(6) ? null : reader.GetString(6);
            var correlationId = reader.IsDBNull(7) ? null : reader.GetString(7);
            var executedUser = reader.IsDBNull(8) ? null : reader.GetString(8);

            var tags = JsonSerializer.Deserialize<List<string>>(tagsJson) ?? [];
            var metadata = new EventMetadata(causationId ?? "", correlationId ?? "", executedUser ?? "");

            return new SerializableEvent(
                Encoding.UTF8.GetBytes(payloadJson),
                sortableUniqueId,
                id,
                metadata,
                tags,
                eventType);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error reading serializable event from SQLite reader");
            return null;
        }
    }

    private string BuildMetaKey(string key) => $"{CurrentServiceId}:{key}";

    private static string BuildMetaKeyPrefix(string serviceId) => $"{serviceId}:";

    private string? GetMetaValue(SqliteConnection connection, string key)
    {
        var serviceKey = BuildMetaKey(key);
        var value = GetMetaValueInternal(connection, serviceKey);

        if (value == null && string.Equals(CurrentServiceId, DefaultServiceIdProvider.DefaultServiceId, StringComparison.Ordinal))
        {
            value = GetMetaValueInternal(connection, key);
        }

        return value;
    }

    private static string? GetMetaValueInternal(SqliteConnection connection, string key)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Value FROM dcb_meta WHERE Key = @key";
        cmd.Parameters.AddWithValue("@key", key);
        return cmd.ExecuteScalar() as string;
    }

    private void SetMetaValue(SqliteConnection connection, string key, string value)
    {
        var serviceKey = BuildMetaKey(key);
        SetMetaValueInternal(connection, serviceKey, value);

        if (string.Equals(CurrentServiceId, DefaultServiceIdProvider.DefaultServiceId, StringComparison.Ordinal))
        {
            SetMetaValueInternal(connection, key, value);
        }
    }

    private static void SetMetaValueInternal(SqliteConnection connection, string key, string value)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO dcb_meta (Key, Value) VALUES (@key, @value)";
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        cmd.ExecuteNonQuery();
    }
}
