using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using ResultBoxes;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Storage.Checkpoints;
using Sekiban.Dcb.Capabilities;

namespace Sekiban.Dcb.Sqlite;

/// <summary>
///     SQLite implementation of IMultiProjectionStateStore. SEK-G20: also the generation/tombstone/exact-token CAS
///     surface (<see cref="IGenerationAwareCheckpointStore" />) via conditional row-count UPDATEs.
///     <para>
///     MIXED-VERSION HAZARD (documented): the legacy <see cref="UpsertFromStreamAsync" /> uses <c>INSERT OR REPLACE</c>,
///     which deletes+reinserts the row and therefore RESETS the control columns to their defaults — a pre-G20 (old)
///     WRITER can thus erase a tombstone. Protection is complete only when every writer is upgraded; the G20 product path
///     never uses the legacy upsert on a capable store (it routes through the CAS methods below).
///     </para>
/// </summary>
public class SqliteMultiProjectionStateStore :
    IMultiProjectionStateStore,
    IStorageDurabilityDescriptorProvider,
    IGenerationAwareCheckpointStore
{
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

    private const string ParamServiceId = "@serviceId";
    private static readonly HashSet<string> AllowedTableNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "dcb_multi_projection_states"
    };
    private static readonly HashSet<string> AllowedColumnNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ServiceId",
        // SEK-G20 additive control-plane columns (generation/tombstone/exact-token CAS).
        "Generation",
        "Revision",
        "Lifecycle"
    };
    private readonly string _connectionString;
    private readonly string _databasePath;
    private readonly ILogger<SqliteMultiProjectionStateStore>? _logger;
    private readonly IServiceIdProvider _serviceIdProvider;

    public SqliteMultiProjectionStateStore(
        string databasePath,
        ILogger<SqliteMultiProjectionStateStore>? logger = null,
        IServiceIdProvider? serviceIdProvider = null)
    {
        _databasePath = databasePath;
        _connectionString = $"Data Source={databasePath}";
        _logger = logger;
        _serviceIdProvider = serviceIdProvider ?? new DefaultServiceIdProvider();

        InitializeDatabase();
    }

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

        EnsureSchema(connection);
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        if (!TableExists(connection, "dcb_multi_projection_states"))
        {
            CreateSchema(connection);
            return;
        }

        if (!HasColumn(connection, "dcb_multi_projection_states", "ServiceId"))
        {
            MigrateSchemaToServiceId(connection);
        }
        else
        {
            EnsureIndexes(connection);
        }

        // SEK-G20 additive upgrade: add the control-plane columns to a pre-G20 table. Existing rows default to 0/0/0
        // (generation 0, revision 0, Active). Additive ALTER only — no event/payload migration.
        EnsureControlColumns(connection);
    }

    private static void EnsureControlColumns(SqliteConnection connection)
    {
        foreach (var column in new[] { "Generation", "Revision", "Lifecycle" })
        {
            if (!HasColumn(connection, "dcb_multi_projection_states", column))
            {
                using var alter = connection.CreateCommand();
                // Identifier validated against AllowedColumnNames by HasColumn above; literal type/default is constant.
                alter.CommandText = $"ALTER TABLE dcb_multi_projection_states ADD COLUMN {column} INTEGER NOT NULL DEFAULT 0;";
                alter.ExecuteNonQuery();
            }
        }
    }

    private static void CreateSchema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS dcb_multi_projection_states (
                ServiceId TEXT NOT NULL,
                ProjectorName TEXT NOT NULL,
                ProjectorVersion TEXT NOT NULL,
                PayloadType TEXT NOT NULL,
                LastSortableUniqueId TEXT NOT NULL,
                EventsProcessed INTEGER NOT NULL,
                StateData BLOB,
                IsOffloaded INTEGER NOT NULL DEFAULT 0,
                OffloadKey TEXT,
                OffloadProvider TEXT,
                OriginalSizeBytes INTEGER NOT NULL,
                CompressedSizeBytes INTEGER NOT NULL,
                SafeWindowThreshold TEXT,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                BuildSource TEXT,
                BuildHost TEXT,
                Generation INTEGER NOT NULL DEFAULT 0,
                Revision INTEGER NOT NULL DEFAULT 0,
                Lifecycle INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (ServiceId, ProjectorName, ProjectorVersion)
            );
            CREATE INDEX IF NOT EXISTS IX_MultiProjectionStates_Service_ProjectorName
            ON dcb_multi_projection_states(ServiceId, ProjectorName);
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
            CREATE INDEX IF NOT EXISTS IX_MultiProjectionStates_Service_ProjectorName
            ON dcb_multi_projection_states(ServiceId, ProjectorName);
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
                CREATE TABLE dcb_multi_projection_states_new (
                    ServiceId TEXT NOT NULL,
                    ProjectorName TEXT NOT NULL,
                    ProjectorVersion TEXT NOT NULL,
                    PayloadType TEXT NOT NULL,
                    LastSortableUniqueId TEXT NOT NULL,
                    EventsProcessed INTEGER NOT NULL,
                    StateData BLOB,
                    IsOffloaded INTEGER NOT NULL DEFAULT 0,
                    OffloadKey TEXT,
                    OffloadProvider TEXT,
                    OriginalSizeBytes INTEGER NOT NULL,
                    CompressedSizeBytes INTEGER NOT NULL,
                    SafeWindowThreshold TEXT,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    BuildSource TEXT,
                    BuildHost TEXT,
                    PRIMARY KEY (ServiceId, ProjectorName, ProjectorVersion)
                );
                """;
            createCmd.ExecuteNonQuery();

            using var copyCmd = connection.CreateCommand();
            copyCmd.Transaction = transaction;
            copyCmd.CommandText = $"""
                INSERT INTO dcb_multi_projection_states_new
                (ServiceId, ProjectorName, ProjectorVersion, PayloadType, LastSortableUniqueId, EventsProcessed,
                 StateData, IsOffloaded, OffloadKey, OffloadProvider, OriginalSizeBytes, CompressedSizeBytes,
                 SafeWindowThreshold, CreatedAt, UpdatedAt, BuildSource, BuildHost)
                SELECT {ParamServiceId}, ProjectorName, ProjectorVersion, PayloadType, LastSortableUniqueId, EventsProcessed,
                       StateData, IsOffloaded, OffloadKey, OffloadProvider, OriginalSizeBytes, CompressedSizeBytes,
                       SafeWindowThreshold, CreatedAt, UpdatedAt, BuildSource, BuildHost
                FROM dcb_multi_projection_states;
                """;
            copyCmd.Parameters.AddWithValue(ParamServiceId, DefaultServiceIdProvider.DefaultServiceId);
            copyCmd.ExecuteNonQuery();

            using var dropCmd = connection.CreateCommand();
            dropCmd.Transaction = transaction;
            dropCmd.CommandText = "DROP TABLE dcb_multi_projection_states;";
            dropCmd.ExecuteNonQuery();

            using var renameCmd = connection.CreateCommand();
            renameCmd.Transaction = transaction;
            renameCmd.CommandText = "ALTER TABLE dcb_multi_projection_states_new RENAME TO dcb_multi_projection_states;";
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

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name = @name";
        cmd.Parameters.AddWithValue("@name", tableName);
        var result = cmd.ExecuteScalar();
        return result != null && result != DBNull.Value;
    }

    private static bool HasColumn(SqliteConnection connection, string tableName, string columnName)
    {
        ValidateSchemaIdentifier(tableName, columnName);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM pragma_table_info(@tableName);";
        cmd.Parameters.AddWithValue("@tableName", tableName);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(0), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateSchemaIdentifier(string tableName, string columnName)
    {
        if (!AllowedTableNames.Contains(tableName) || !AllowedColumnNames.Contains(columnName))
        {
            throw new ArgumentException($"Unsupported schema identifier: {tableName}.{columnName}");
        }
    }

    public async Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestForVersionAsync(
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var serviceId = CurrentServiceId;
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"""
                SELECT ProjectorName, ProjectorVersion, PayloadType, LastSortableUniqueId, EventsProcessed,
                       StateData, IsOffloaded, OffloadKey, OffloadProvider, OriginalSizeBytes, CompressedSizeBytes,
                       SafeWindowThreshold, CreatedAt, UpdatedAt, BuildSource, BuildHost
                FROM dcb_multi_projection_states
                WHERE ServiceId = {ParamServiceId} AND ProjectorName = @projectorName AND ProjectorVersion = @projectorVersion
                """;
            cmd.Parameters.AddWithValue("@projectorName", projectorName);
            cmd.Parameters.AddWithValue("@projectorVersion", projectorVersion);
            cmd.Parameters.AddWithValue(ParamServiceId, serviceId);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            if (await reader.ReadAsync(cancellationToken))
            {
                var record = ReadRecord(reader);
                return ResultBox.FromValue(OptionalValue.FromValue(record));
            }

            return ResultBox.FromValue(OptionalValue<MultiProjectionStateRecord>.Empty);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error getting projection state for {ProjectorName}:{ProjectorVersion}", projectorName, projectorVersion);
            return ResultBox.Error<OptionalValue<MultiProjectionStateRecord>>(ex);
        }
    }

    public async Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestAnyVersionAsync(
        string projectorName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var serviceId = CurrentServiceId;
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"""
                SELECT ProjectorName, ProjectorVersion, PayloadType, LastSortableUniqueId, EventsProcessed,
                       StateData, IsOffloaded, OffloadKey, OffloadProvider, OriginalSizeBytes, CompressedSizeBytes,
                       SafeWindowThreshold, CreatedAt, UpdatedAt, BuildSource, BuildHost
                FROM dcb_multi_projection_states
                WHERE ServiceId = {ParamServiceId} AND ProjectorName = @projectorName
                ORDER BY EventsProcessed DESC
                LIMIT 1
                """;
            cmd.Parameters.AddWithValue("@projectorName", projectorName);
            cmd.Parameters.AddWithValue(ParamServiceId, serviceId);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            if (await reader.ReadAsync(cancellationToken))
            {
                var record = ReadRecord(reader);
                return ResultBox.FromValue(OptionalValue.FromValue(record));
            }

            return ResultBox.FromValue(OptionalValue<MultiProjectionStateRecord>.Empty);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error getting latest projection state for {ProjectorName}", projectorName);
            return ResultBox.Error<OptionalValue<MultiProjectionStateRecord>>(ex);
        }
    }

    public Task<ResultBox<bool>> UpsertAsync(
        MultiProjectionStateRecord record,
        int offloadThresholdBytes = 1_000_000,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return Task.FromResult(ResultBox.Error<bool>(
            new NotSupportedException(
                "SqliteMultiProjectionStateStore requires payload stream upsert. Use UpsertFromStreamAsync.")));
    }

    public async Task<ResultBox<bool>> UpsertFromStreamAsync(
        MultiProjectionStateWriteRequest request,
        Stream stream,
        int offloadThresholdBytes,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (request.IsOffloaded)
            {
                return ResultBox.Error<bool>(
                    new NotSupportedException(
                        "SqliteMultiProjectionStateStore does not support offloaded snapshot metadata. Provide inline stream payload."));
            }

            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            var stateData = ms.ToArray();

            var serviceId = CurrentServiceId;
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"""
                INSERT OR REPLACE INTO dcb_multi_projection_states
                (ServiceId, ProjectorName, ProjectorVersion, PayloadType, LastSortableUniqueId, EventsProcessed,
                 StateData, IsOffloaded, OffloadKey, OffloadProvider, OriginalSizeBytes, CompressedSizeBytes,
                 SafeWindowThreshold, CreatedAt, UpdatedAt, BuildSource, BuildHost)
                VALUES
                ({ParamServiceId}, @projectorName, @projectorVersion, @payloadType, @lastSortableUniqueId, @eventsProcessed,
                 @stateData, @isOffloaded, @offloadKey, @offloadProvider, @originalSizeBytes, @compressedSizeBytes,
                 @safeWindowThreshold, @createdAt, @updatedAt, @buildSource, @buildHost)
                """;

            cmd.Parameters.AddWithValue(ParamServiceId, serviceId);
            cmd.Parameters.AddWithValue("@projectorName", request.ProjectorName);
            cmd.Parameters.AddWithValue("@projectorVersion", request.ProjectorVersion);
            cmd.Parameters.AddWithValue("@payloadType", request.PayloadType);
            cmd.Parameters.AddWithValue("@lastSortableUniqueId", request.LastSortableUniqueId);
            cmd.Parameters.AddWithValue("@eventsProcessed", request.EventsProcessed);
            cmd.Parameters.AddWithValue("@stateData", stateData);
            cmd.Parameters.AddWithValue("@isOffloaded", 0);
            cmd.Parameters.AddWithValue("@offloadKey", DBNull.Value);
            cmd.Parameters.AddWithValue("@offloadProvider", DBNull.Value);
            cmd.Parameters.AddWithValue("@originalSizeBytes", request.OriginalSizeBytes);
            cmd.Parameters.AddWithValue("@compressedSizeBytes", request.CompressedSizeBytes);
            cmd.Parameters.AddWithValue("@safeWindowThreshold", request.SafeWindowThreshold);
            cmd.Parameters.AddWithValue("@createdAt", request.CreatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("@updatedAt", request.UpdatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("@buildSource", (object?)request.BuildSource ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@buildHost", (object?)request.BuildHost ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return ResultBox.FromValue(true);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error upserting projection state stream for {ProjectorName}:{ProjectorVersion}",
                request.ProjectorName, request.ProjectorVersion);
            return ResultBox.Error<bool>(ex);
        }
    }

    public async Task<ResultBox<Stream>> OpenStateDataReadStreamAsync(
        MultiProjectionStateRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var serviceId = CurrentServiceId;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT StateData
            FROM dcb_multi_projection_states
            WHERE ServiceId = {ParamServiceId} AND ProjectorName = @projectorName AND ProjectorVersion = @projectorVersion
            """;
        cmd.Parameters.AddWithValue(ParamServiceId, serviceId);
        cmd.Parameters.AddWithValue("@projectorName", record.ProjectorName);
        cmd.Parameters.AddWithValue("@projectorVersion", record.ProjectorVersion);

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result is byte[] data)
        {
            return ResultBox.FromValue<Stream>(new MemoryStream(data, writable: false));
        }

        return ResultBox.Error<Stream>(
            new InvalidOperationException(
                $"StateData not found for {record.ProjectorName}/{record.ProjectorVersion} in sqlite store."));
    }

    public async Task<ResultBox<IReadOnlyList<ProjectorStateInfo>>> ListAllAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var states = new List<ProjectorStateInfo>();
            var serviceId = CurrentServiceId;

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"""
                SELECT ProjectorName, ProjectorVersion, EventsProcessed, UpdatedAt,
                       OriginalSizeBytes, CompressedSizeBytes, LastSortableUniqueId
                FROM dcb_multi_projection_states
                WHERE ServiceId = {ParamServiceId}
                ORDER BY ProjectorName, ProjectorVersion
                """;
            cmd.Parameters.AddWithValue(ParamServiceId, serviceId);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var projectorName = reader.GetString(0);
                var projectorVersion = reader.GetString(1);
                var eventsProcessed = reader.GetInt64(2);
                var updatedAt = DateTime.Parse(reader.GetString(3));
                var originalSizeBytes = reader.GetInt64(4);
                var compressedSizeBytes = reader.GetInt64(5);
                var lastSortableUniqueId = reader.GetString(6);

                states.Add(new ProjectorStateInfo(
                    projectorName,
                    projectorVersion,
                    eventsProcessed,
                    updatedAt,
                    originalSizeBytes,
                    compressedSizeBytes,
                    lastSortableUniqueId));
            }

            return ResultBox.FromValue<IReadOnlyList<ProjectorStateInfo>>(states);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error listing all projection states");
            return ResultBox.Error<IReadOnlyList<ProjectorStateInfo>>(ex);
        }
    }

    public async Task<ResultBox<bool>> DeleteAsync(
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var serviceId = CurrentServiceId;
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"""
                DELETE FROM dcb_multi_projection_states
                WHERE ServiceId = {ParamServiceId} AND ProjectorName = @projectorName AND ProjectorVersion = @projectorVersion
                """;
            cmd.Parameters.AddWithValue("@projectorName", projectorName);
            cmd.Parameters.AddWithValue("@projectorVersion", projectorVersion);
            cmd.Parameters.AddWithValue(ParamServiceId, serviceId);

            var deleted = await cmd.ExecuteNonQueryAsync(cancellationToken);
            return ResultBox.FromValue(deleted > 0);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error deleting projection state for {ProjectorName}:{ProjectorVersion}",
                projectorName, projectorVersion);
            return ResultBox.Error<bool>(ex);
        }
    }

    public async Task<ResultBox<int>> DeleteAllAsync(
        string? projectorName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var serviceId = CurrentServiceId;
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var cmd = connection.CreateCommand();
            if (!string.IsNullOrEmpty(projectorName))
            {
                cmd.CommandText = $"""
                    DELETE FROM dcb_multi_projection_states
                    WHERE ServiceId = {ParamServiceId} AND ProjectorName = @projectorName
                    """;
                cmd.Parameters.AddWithValue("@projectorName", projectorName);
            }
            else
            {
                cmd.CommandText = $"DELETE FROM dcb_multi_projection_states WHERE ServiceId = {ParamServiceId}";
            }
            cmd.Parameters.AddWithValue(ParamServiceId, serviceId);

            var deleted = await cmd.ExecuteNonQueryAsync(cancellationToken);
            return ResultBox.FromValue(deleted);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error deleting all projection states");
            return ResultBox.Error<int>(ex);
        }
    }

    private static MultiProjectionStateRecord ReadRecord(SqliteDataReader reader)
    {
        return new MultiProjectionStateRecord(
            ProjectorName: reader.GetString(0),
            ProjectorVersion: reader.GetString(1),
            PayloadType: reader.GetString(2),
            LastSortableUniqueId: reader.GetString(3),
            EventsProcessed: reader.GetInt64(4),
            IsOffloaded: reader.GetInt32(6) == 1,
            OffloadKey: reader.IsDBNull(7) ? null : reader.GetString(7),
            OffloadProvider: reader.IsDBNull(8) ? null : reader.GetString(8),
            OriginalSizeBytes: reader.GetInt64(9),
            CompressedSizeBytes: reader.GetInt64(10),
            SafeWindowThreshold: reader.GetString(11),
            CreatedAt: DateTime.Parse(reader.GetString(12)),
            UpdatedAt: DateTime.Parse(reader.GetString(13)),
            BuildSource: reader.IsDBNull(14) ? "" : reader.GetString(14),
            BuildHost: reader.IsDBNull(15) ? null : reader.GetString(15));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // SEK-G20 generation/tombstone/exact-token CAS (SQLite native, conditional row-count UPDATEs)
    // ---------------------------------------------------------------------------------------------------------------

    public CheckpointStoreCapabilityDescriptor DescribeCheckpointCapability() =>
        CheckpointStoreCapabilityDescriptor.Supporting("Sqlite", CheckpointCapabilityKind.GenerationTombstoneCas);

    private static bool TryToken(CheckpointExpectation e, out long revision) => long.TryParse(e.ExpectedRevision, out revision);

    public async Task<ResultBox<CheckpointSlot>> ReadCheckpointSlotAsync(
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var serviceId = CurrentServiceId;
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"""
                SELECT ProjectorName, ProjectorVersion, PayloadType, LastSortableUniqueId, EventsProcessed,
                       StateData, IsOffloaded, OffloadKey, OffloadProvider, OriginalSizeBytes, CompressedSizeBytes,
                       SafeWindowThreshold, CreatedAt, UpdatedAt, BuildSource, BuildHost, Generation, Revision, Lifecycle
                FROM dcb_multi_projection_states
                WHERE ServiceId = {ParamServiceId} AND ProjectorName = @projectorName AND ProjectorVersion = @projectorVersion
                """;
            cmd.Parameters.AddWithValue(ParamServiceId, serviceId);
            cmd.Parameters.AddWithValue("@projectorName", projectorName);
            cmd.Parameters.AddWithValue("@projectorVersion", projectorVersion);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return ResultBox.FromValue(CheckpointSlot.Absent);
            }
            var record = ReadRecord(reader);
            var generation = reader.GetInt64(16);
            var revision = reader.GetInt64(17);
            var lifecycle = (CheckpointLifecycle)reader.GetInt32(18);
            return ResultBox.FromValue(new CheckpointSlot(true, generation, revision.ToString(), lifecycle, record));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<CheckpointSlot>(ex);
        }
    }

    private async Task<CheckpointSlot> RefetchAsync(string projectorName, string projectorVersion, CancellationToken ct)
    {
        var read = await ReadCheckpointSlotAsync(projectorName, projectorVersion, ct);
        return read.IsSuccess ? read.GetValue() : CheckpointSlot.Absent;
    }

    private static void BindPayloadParams(SqliteCommand cmd, MultiProjectionStateWriteRequest r, byte[] stateData)
    {
        cmd.Parameters.AddWithValue("@payloadType", r.PayloadType);
        cmd.Parameters.AddWithValue("@lastSortableUniqueId", r.LastSortableUniqueId);
        cmd.Parameters.AddWithValue("@eventsProcessed", r.EventsProcessed);
        cmd.Parameters.AddWithValue("@stateData", stateData);
        cmd.Parameters.AddWithValue("@originalSizeBytes", r.OriginalSizeBytes);
        cmd.Parameters.AddWithValue("@compressedSizeBytes", r.CompressedSizeBytes);
        cmd.Parameters.AddWithValue("@safeWindowThreshold", r.SafeWindowThreshold);
        cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@buildSource", (object?)r.BuildSource ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@buildHost", (object?)r.BuildHost ?? DBNull.Value);
    }

    public async Task<CheckpointCasOutcome> ConditionalUpsertAsync(
        MultiProjectionStateWriteRequest payload,
        Stream stream,
        CheckpointExpectation expectation,
        int offloadThresholdBytes,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            var stateData = ms.ToArray();

            var serviceId = CurrentServiceId;
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            if (expectation.ExpectAbsent)
            {
                // Expected-absence create as a plain INSERT with ON CONFLICT DO NOTHING; changes()==1 means we created it.
                await using var insert = connection.CreateCommand();
                insert.CommandText = $"""
                    INSERT INTO dcb_multi_projection_states
                    (ServiceId, ProjectorName, ProjectorVersion, PayloadType, LastSortableUniqueId, EventsProcessed,
                     StateData, IsOffloaded, OffloadKey, OffloadProvider, OriginalSizeBytes, CompressedSizeBytes,
                     SafeWindowThreshold, CreatedAt, UpdatedAt, BuildSource, BuildHost, Generation, Revision, Lifecycle)
                    VALUES
                    ({ParamServiceId}, @projectorName, @projectorVersion, @payloadType, @lastSortableUniqueId, @eventsProcessed,
                     @stateData, 0, NULL, NULL, @originalSizeBytes, @compressedSizeBytes,
                     @safeWindowThreshold, @createdAt, @updatedAt, @buildSource, @buildHost, 0, 1, 0)
                    ON CONFLICT(ServiceId, ProjectorName, ProjectorVersion) DO NOTHING
                    """;
                insert.Parameters.AddWithValue(ParamServiceId, serviceId);
                insert.Parameters.AddWithValue("@projectorName", payload.ProjectorName);
                insert.Parameters.AddWithValue("@projectorVersion", payload.ProjectorVersion);
                insert.Parameters.AddWithValue("@createdAt", payload.CreatedAt.ToString("O"));
                BindPayloadParams(insert, payload, stateData);
                var inserted = await insert.ExecuteNonQueryAsync(cancellationToken);
                var slot0 = await RefetchAsync(payload.ProjectorName, payload.ProjectorVersion, cancellationToken);
                return inserted == 1 ? CheckpointCasOutcome.Committed(slot0) : CheckpointCasOutcome.Rejected(slot0);
            }

            if (!TryToken(expectation, out var expectedRevision))
            {
                return CheckpointCasOutcome.Corrupt();
            }

            await using var update = connection.CreateCommand();
            update.CommandText = $"""
                UPDATE dcb_multi_projection_states
                SET PayloadType = @payloadType, LastSortableUniqueId = @lastSortableUniqueId, EventsProcessed = @eventsProcessed,
                    StateData = @stateData, IsOffloaded = 0, OffloadKey = NULL, OffloadProvider = NULL,
                    OriginalSizeBytes = @originalSizeBytes, CompressedSizeBytes = @compressedSizeBytes,
                    SafeWindowThreshold = @safeWindowThreshold, UpdatedAt = @updatedAt, BuildSource = @buildSource,
                    BuildHost = @buildHost, Revision = Revision + 1
                WHERE ServiceId = {ParamServiceId} AND ProjectorName = @projectorName AND ProjectorVersion = @projectorVersion
                    AND Generation = @g AND Revision = @rev AND Lifecycle = 0
                """;
            update.Parameters.AddWithValue(ParamServiceId, serviceId);
            update.Parameters.AddWithValue("@projectorName", payload.ProjectorName);
            update.Parameters.AddWithValue("@projectorVersion", payload.ProjectorVersion);
            update.Parameters.AddWithValue("@g", expectation.ExpectedGeneration);
            update.Parameters.AddWithValue("@rev", expectedRevision);
            BindPayloadParams(update, payload, stateData);
            var affected = await update.ExecuteNonQueryAsync(cancellationToken);
            var slot = await RefetchAsync(payload.ProjectorName, payload.ProjectorVersion, cancellationToken);
            return affected == 1 ? CheckpointCasOutcome.Committed(slot) : CheckpointCasOutcome.Rejected(slot);
        }
        catch (Exception ex)
        {
            // SEK-G20: a dispatch/transport failure whose commit is UNKNOWN — resolve via a bounded independent re-read.
            return await ResolveWriteInDoubtAsync(payload, expectation.ExpectAbsent ? 0 : expectation.ExpectedGeneration, ex);
        }
    }

    public async Task<CheckpointCasOutcome> InvalidateWithTombstoneAsync(
        string projectorName,
        string projectorVersion,
        CheckpointExpectation expectation,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (expectation.ExpectAbsent || !TryToken(expectation, out var expectedRevision))
            {
                return CheckpointCasOutcome.Corrupt();
            }
            var serviceId = CurrentServiceId;
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"""
                UPDATE dcb_multi_projection_states
                SET Generation = Generation + 1, Revision = Revision + 1, Lifecycle = 1, UpdatedAt = @updatedAt
                WHERE ServiceId = {ParamServiceId} AND ProjectorName = @projectorName AND ProjectorVersion = @projectorVersion
                    AND Generation = @g AND Revision = @rev AND Lifecycle = 0
                """;
            cmd.Parameters.AddWithValue(ParamServiceId, serviceId);
            cmd.Parameters.AddWithValue("@projectorName", projectorName);
            cmd.Parameters.AddWithValue("@projectorVersion", projectorVersion);
            cmd.Parameters.AddWithValue("@g", expectation.ExpectedGeneration);
            cmd.Parameters.AddWithValue("@rev", expectedRevision);
            cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
            var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
            var slot = await RefetchAsync(projectorName, projectorVersion, cancellationToken);
            return affected == 1 ? CheckpointCasOutcome.Committed(slot) : CheckpointCasOutcome.Rejected(slot);
        }
        catch (Exception ex)
        {
            return CheckpointCasOutcome.ProviderFailed(ex);
        }
    }

    public async Task<CheckpointCasOutcome> CommitRebuiltAsync(
        MultiProjectionStateWriteRequest payload,
        Stream stream,
        CheckpointExpectation expectation,
        int offloadThresholdBytes,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (expectation.ExpectAbsent || !TryToken(expectation, out var expectedRevision))
            {
                return CheckpointCasOutcome.Corrupt();
            }
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            var stateData = ms.ToArray();

            var serviceId = CurrentServiceId;
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var cmd = connection.CreateCommand();
            // One atomic same-row CAS: write rebuilt payload AND clear the tombstone on the exact Tombstoned token.
            cmd.CommandText = $"""
                UPDATE dcb_multi_projection_states
                SET PayloadType = @payloadType, LastSortableUniqueId = @lastSortableUniqueId, EventsProcessed = @eventsProcessed,
                    StateData = @stateData, IsOffloaded = 0, OffloadKey = NULL, OffloadProvider = NULL,
                    OriginalSizeBytes = @originalSizeBytes, CompressedSizeBytes = @compressedSizeBytes,
                    SafeWindowThreshold = @safeWindowThreshold, UpdatedAt = @updatedAt, BuildSource = @buildSource,
                    BuildHost = @buildHost, Lifecycle = 0, Revision = Revision + 1
                WHERE ServiceId = {ParamServiceId} AND ProjectorName = @projectorName AND ProjectorVersion = @projectorVersion
                    AND Generation = @g AND Revision = @rev AND Lifecycle = 1
                """;
            cmd.Parameters.AddWithValue(ParamServiceId, serviceId);
            cmd.Parameters.AddWithValue("@projectorName", payload.ProjectorName);
            cmd.Parameters.AddWithValue("@projectorVersion", payload.ProjectorVersion);
            cmd.Parameters.AddWithValue("@g", expectation.ExpectedGeneration);
            cmd.Parameters.AddWithValue("@rev", expectedRevision);
            BindPayloadParams(cmd, payload, stateData);
            var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
            var slot = await RefetchAsync(payload.ProjectorName, payload.ProjectorVersion, cancellationToken);
            return affected == 1 ? CheckpointCasOutcome.Committed(slot) : CheckpointCasOutcome.Rejected(slot);
        }
        catch (Exception ex)
        {
            // SEK-G20: a dispatch/transport failure whose commit is UNKNOWN — resolve via a bounded independent re-read.
            return await ResolveWriteInDoubtAsync(payload, expectation.ExpectedGeneration, ex);
        }
    }

    // Resolves a write whose response was lost: a bounded re-read that verifies our exact resulting generation + Active
    // lifecycle + payload identity reports Committed; otherwise typed retryable InDoubt (retry with the same exact token
    // is idempotent). Never a false Committed — a concurrent winner with a different payload fails identity.
    private Task<CheckpointCasOutcome> ResolveWriteInDoubtAsync(
        MultiProjectionStateWriteRequest payload, long resultingGeneration, Exception cause) =>
        CheckpointInDoubtResolver.ResolveAsync(
            ct => ReadCheckpointSlotAsync(payload.ProjectorName, payload.ProjectorVersion, ct),
            slot => slot.IsActive && slot.Generation == resultingGeneration && slot.Record is { } r
                && string.Equals(r.LastSortableUniqueId, payload.LastSortableUniqueId, StringComparison.Ordinal)
                && r.EventsProcessed == payload.EventsProcessed,
            maxAttempts: 3,
            cause: cause);
}
