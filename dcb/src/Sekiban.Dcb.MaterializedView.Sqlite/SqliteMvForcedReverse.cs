using Microsoft.Data.Sqlite;

namespace Sekiban.Dcb.MaterializedView.Sqlite;

public sealed partial class SqliteMvRegistryStore
{
    private static readonly MvForcedReverseSqlPlan ForcedReverseSql = MvForcedReverseSqlPlan.Create(
        candidateFenceSql: """
            UPDATE sekiban_mv_registry SET last_updated = last_updated
            WHERE service_id = @ServiceId AND view_name = @ViewName AND view_version = @ViewVersion;
            """,
        fenceReturnsRows: false,
        savepointSql: "SAVEPOINT sekiban_mv_forced_reverse;",
        rollbackSavepointSql: "ROLLBACK TO SAVEPOINT sekiban_mv_forced_reverse;",
        releaseSavepointSql: "RELEASE SAVEPOINT sekiban_mv_forced_reverse;");

    protected override MvForcedReverseSqlPlan ForcedReversePlan => ForcedReverseSql;
    protected override SqliteConnection CreateForcedReverseConnection() => new(_connectionString);
    protected override object FormatForcedReverseTimestamp(DateTimeOffset value) =>
        SerializeDate(value) ?? throw new InvalidOperationException("A forced-reverse timestamp is required.");
}
