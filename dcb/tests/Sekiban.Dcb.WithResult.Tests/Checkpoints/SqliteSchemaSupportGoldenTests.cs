using Microsoft.Data.Sqlite;
using Sekiban.Dcb.Sqlite;
using Xunit;
namespace Sekiban.Dcb.Tests.Checkpoints;

/// <summary>
///     GOLDEN pins for the shared SQLite schema probes extracted from both SQLite stores (SEK-G20 dedup). The exact SQL
///     strings and the identifier-allowlist behavior are frozen here so the de-duplication is provably behavior-preserving:
///     a change to the probe SQL byte or the allowlist semantics fails this test.
/// </summary>
public class SqliteSchemaSupportGoldenTests
{
    private static readonly IReadOnlySet<string> Tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dcb_multi_projection_states" };
    private static readonly IReadOnlySet<string> Columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ServiceId", "Generation" };

    [Fact]
    public void TheProbeSql_IsFrozen()
    {
        Assert.Equal("SELECT name FROM sqlite_master WHERE type='table' AND name = @name", SqliteSchemaSupport.TableExistsSql);
        Assert.Equal("SELECT name FROM pragma_table_info(@tableName);", SqliteSchemaSupport.ColumnsSql);
    }

    [Fact]
    public void TableExists_And_HasColumn_ReflectTheRealSchema()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE dcb_multi_projection_states (ServiceId TEXT, Generation INTEGER)";
            create.ExecuteNonQuery();
        }

        Assert.True(SqliteSchemaSupport.TableExists(connection, "dcb_multi_projection_states"));
        Assert.False(SqliteSchemaSupport.TableExists(connection, "missing_table"));
        Assert.True(SqliteSchemaSupport.HasColumn(connection, "dcb_multi_projection_states", "ServiceId", Tables, Columns));
        Assert.True(SqliteSchemaSupport.HasColumn(connection, "dcb_multi_projection_states", "Generation", Tables, Columns));
    }

    [Fact]
    public void HasColumn_RejectsAnIdentifierOutsideTheAllowlist_FailClosed()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        // An identifier that is not in the per-store allowlist is refused BEFORE any SQL touches it — the anti-injection guard.
        Assert.Throws<ArgumentException>(() =>
            SqliteSchemaSupport.HasColumn(connection, "dcb_multi_projection_states", "DROP TABLE x", Tables, Columns));
        Assert.Throws<ArgumentException>(() =>
            SqliteSchemaSupport.HasColumn(connection, "unknown_table", "ServiceId", Tables, Columns));
    }
}
