using System.Reflection;
using Npgsql;

namespace DcbOrleans.WithoutResult.ApiService;

/// <summary>
/// Initializes Orleans PostgreSQL schema at startup if it doesn't exist.
/// </summary>
public class OrleansSchemaInitializer
{
    private readonly ILogger<OrleansSchemaInitializer> _logger;

    public OrleansSchemaInitializer(ILogger<OrleansSchemaInitializer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Initialize Orleans schema if the required tables don't exist.
    /// </summary>
    public async Task InitializeAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Checking Orleans schema...");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Check if OrleansQuery table exists and has required queries
        var schemaValid = await CheckSchemaValidAsync(connection, cancellationToken);

        if (schemaValid)
        {
            _logger.LogInformation("Orleans schema is valid and up to date.");
            return;
        }

        _logger.LogInformation("Orleans schema missing or outdated. Initializing...");

        // Drop existing Orleans tables if they exist (to handle schema upgrades)
        await DropExistingTablesAsync(connection, cancellationToken);

        // Run scripts in order
        var scripts = new[]
        {
            "PostgreSQL-Main.sql",
            "PostgreSQL-Clustering.sql",
            "PostgreSQL-Persistence.sql",
            "PostgreSQL-Reminders.sql"
        };

        foreach (var scriptName in scripts)
        {
            _logger.LogInformation("Running {Script}...", scriptName);
            var sql = await LoadEmbeddedScriptAsync(scriptName);
            await ExecuteScriptAsync(connection, sql, cancellationToken);
        }

        _logger.LogInformation("Orleans schema initialization completed.");
    }

    private static async Task<bool> CheckSchemaValidAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        // Check if OrleansQuery table exists
        const string tableCheckSql = @"
            SELECT EXISTS (
                SELECT FROM information_schema.tables
                WHERE table_schema = 'public'
                AND table_name = 'orleansquery'
            );";

        await using var tableCmd = new NpgsqlCommand(tableCheckSql, connection);
        var tableExists = await tableCmd.ExecuteScalarAsync(cancellationToken) is true;

        if (!tableExists)
        {
            return false;
        }

        // Check if required query exists (CleanupDefunctSiloEntriesKey is required by Orleans 9.x)
        const string queryCheckSql = @"
            SELECT COUNT(*) FROM OrleansQuery
            WHERE QueryKey = 'CleanupDefunctSiloEntriesKey';";

        try
        {
            await using var queryCmd = new NpgsqlCommand(queryCheckSql, connection);
            var count = await queryCmd.ExecuteScalarAsync(cancellationToken);
            return count is long l && l > 0;
        }
        catch
        {
            // Table might exist but query check failed - schema needs update
            return false;
        }
    }

    private async Task DropExistingTablesAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        // Drop Orleans functions first
        var dropFunctions = new[]
        {
            "DROP FUNCTION IF EXISTS update_i_am_alive_time CASCADE;",
            "DROP FUNCTION IF EXISTS insert_membership_version CASCADE;",
            "DROP FUNCTION IF EXISTS insert_membership CASCADE;",
            "DROP FUNCTION IF EXISTS update_membership CASCADE;",
            "DROP FUNCTION IF EXISTS delete_membership CASCADE;",
            "DROP FUNCTION IF EXISTS delete_membership_table_entries CASCADE;",
            "DROP FUNCTION IF EXISTS read_membership CASCADE;",
            "DROP FUNCTION IF EXISTS read_all_membership CASCADE;",
            "DROP FUNCTION IF EXISTS gateways_to_silos CASCADE;",
            "DROP FUNCTION IF EXISTS membership_entry_to_row CASCADE;",
            "DROP FUNCTION IF EXISTS cleanup_defunct_silo_entries CASCADE;",
            "DROP FUNCTION IF EXISTS upsert_reminder_row CASCADE;",
            "DROP FUNCTION IF EXISTS read_reminder_row CASCADE;",
            "DROP FUNCTION IF EXISTS read_reminder_rows CASCADE;",
            "DROP FUNCTION IF EXISTS delete_reminder_row CASCADE;",
            "DROP FUNCTION IF EXISTS delete_reminder_rows CASCADE;",
            "DROP FUNCTION IF EXISTS write_to_storage CASCADE;",
            "DROP FUNCTION IF EXISTS clear_storage CASCADE;",
            "DROP FUNCTION IF EXISTS read_from_storage CASCADE;"
        };

        foreach (var dropFunc in dropFunctions)
        {
            try
            {
                await using var cmd = new NpgsqlCommand(dropFunc, connection);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            catch
            {
                // Ignore errors for non-existent functions
            }
        }

        // Drop Orleans tables in correct order (respecting foreign keys)
        var dropCommands = new[]
        {
            "DROP TABLE IF EXISTS OrleansRemindersTable CASCADE;",
            "DROP TABLE IF EXISTS OrleansStorage CASCADE;",
            "DROP TABLE IF EXISTS OrleansMembershipTable CASCADE;",
            "DROP TABLE IF EXISTS OrleansMembershipVersionTable CASCADE;",
            "DROP TABLE IF EXISTS OrleansQuery CASCADE;"
        };

        foreach (var dropCmd in dropCommands)
        {
            _logger.LogInformation("Executing: {Sql}", dropCmd);
            await using var cmd = new NpgsqlCommand(dropCmd, connection);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<string> LoadEmbeddedScriptAsync(string scriptName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(scriptName, StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
        {
            throw new InvalidOperationException($"Embedded resource not found: {scriptName}");
        }

        await using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            throw new InvalidOperationException($"Could not open stream for: {resourceName}");
        }

        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static async Task ExecuteScriptAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        // Split by GO or semicolon for batch execution
        // PostgreSQL doesn't use GO, but some scripts might have multiple statements
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.CommandTimeout = 120; // 2 minutes timeout for schema creation
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
