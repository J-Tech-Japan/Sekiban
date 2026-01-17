using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using ResultBoxes;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.Sqlite;

/// <summary>
///     SQLite implementation of IMultiProjectionStateStore
/// </summary>
public class SqliteMultiProjectionStateStore : IMultiProjectionStateStore
{
    private readonly string _connectionString;
    private readonly string _databasePath;
    private readonly ILogger<SqliteMultiProjectionStateStore>? _logger;

    public SqliteMultiProjectionStateStore(
        string databasePath,
        ILogger<SqliteMultiProjectionStateStore>? logger = null)
    {
        _databasePath = databasePath;
        _connectionString = $"Data Source={databasePath}";
        _logger = logger;

        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS dcb_multi_projection_states (
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
                PRIMARY KEY (ProjectorName, ProjectorVersion)
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public async Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestForVersionAsync(
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT ProjectorName, ProjectorVersion, PayloadType, LastSortableUniqueId, EventsProcessed,
                       StateData, IsOffloaded, OffloadKey, OffloadProvider, OriginalSizeBytes, CompressedSizeBytes,
                       SafeWindowThreshold, CreatedAt, UpdatedAt, BuildSource, BuildHost
                FROM dcb_multi_projection_states
                WHERE ProjectorName = @projectorName AND ProjectorVersion = @projectorVersion
                """;
            cmd.Parameters.AddWithValue("@projectorName", projectorName);
            cmd.Parameters.AddWithValue("@projectorVersion", projectorVersion);

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
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT ProjectorName, ProjectorVersion, PayloadType, LastSortableUniqueId, EventsProcessed,
                       StateData, IsOffloaded, OffloadKey, OffloadProvider, OriginalSizeBytes, CompressedSizeBytes,
                       SafeWindowThreshold, CreatedAt, UpdatedAt, BuildSource, BuildHost
                FROM dcb_multi_projection_states
                WHERE ProjectorName = @projectorName
                ORDER BY EventsProcessed DESC
                LIMIT 1
                """;
            cmd.Parameters.AddWithValue("@projectorName", projectorName);

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

    public async Task<ResultBox<bool>> UpsertAsync(
        MultiProjectionStateRecord record,
        int offloadThresholdBytes = 1_000_000,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT OR REPLACE INTO dcb_multi_projection_states
                (ProjectorName, ProjectorVersion, PayloadType, LastSortableUniqueId, EventsProcessed,
                 StateData, IsOffloaded, OffloadKey, OffloadProvider, OriginalSizeBytes, CompressedSizeBytes,
                 SafeWindowThreshold, CreatedAt, UpdatedAt, BuildSource, BuildHost)
                VALUES
                (@projectorName, @projectorVersion, @payloadType, @lastSortableUniqueId, @eventsProcessed,
                 @stateData, @isOffloaded, @offloadKey, @offloadProvider, @originalSizeBytes, @compressedSizeBytes,
                 @safeWindowThreshold, @createdAt, @updatedAt, @buildSource, @buildHost)
                """;

            cmd.Parameters.AddWithValue("@projectorName", record.ProjectorName);
            cmd.Parameters.AddWithValue("@projectorVersion", record.ProjectorVersion);
            cmd.Parameters.AddWithValue("@payloadType", record.PayloadType);
            cmd.Parameters.AddWithValue("@lastSortableUniqueId", record.LastSortableUniqueId);
            cmd.Parameters.AddWithValue("@eventsProcessed", record.EventsProcessed);
            cmd.Parameters.AddWithValue("@stateData", (object?)record.StateData ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@isOffloaded", record.IsOffloaded ? 1 : 0);
            cmd.Parameters.AddWithValue("@offloadKey", (object?)record.OffloadKey ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@offloadProvider", (object?)record.OffloadProvider ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@originalSizeBytes", record.OriginalSizeBytes);
            cmd.Parameters.AddWithValue("@compressedSizeBytes", record.CompressedSizeBytes);
            cmd.Parameters.AddWithValue("@safeWindowThreshold", record.SafeWindowThreshold);
            cmd.Parameters.AddWithValue("@createdAt", record.CreatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("@updatedAt", record.UpdatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("@buildSource", (object?)record.BuildSource ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@buildHost", (object?)record.BuildHost ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync(cancellationToken);

            return ResultBox.FromValue(true);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error upserting projection state for {ProjectorName}:{ProjectorVersion}",
                record.ProjectorName, record.ProjectorVersion);
            return ResultBox.Error<bool>(ex);
        }
    }

    public async Task<ResultBox<IReadOnlyList<ProjectorStateInfo>>> ListAllAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var states = new List<ProjectorStateInfo>();

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT ProjectorName, ProjectorVersion, EventsProcessed, UpdatedAt,
                       OriginalSizeBytes, CompressedSizeBytes, LastSortableUniqueId
                FROM dcb_multi_projection_states
                ORDER BY ProjectorName, ProjectorVersion
                """;

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
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                DELETE FROM dcb_multi_projection_states
                WHERE ProjectorName = @projectorName AND ProjectorVersion = @projectorVersion
                """;
            cmd.Parameters.AddWithValue("@projectorName", projectorName);
            cmd.Parameters.AddWithValue("@projectorVersion", projectorVersion);

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
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var cmd = connection.CreateCommand();
            if (!string.IsNullOrEmpty(projectorName))
            {
                cmd.CommandText = "DELETE FROM dcb_multi_projection_states WHERE ProjectorName = @projectorName";
                cmd.Parameters.AddWithValue("@projectorName", projectorName);
            }
            else
            {
                cmd.CommandText = "DELETE FROM dcb_multi_projection_states";
            }

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
            StateData: reader.IsDBNull(5) ? null : (byte[])reader.GetValue(5),
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
}
