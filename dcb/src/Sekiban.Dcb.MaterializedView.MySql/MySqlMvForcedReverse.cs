using MySqlConnector;

namespace Sekiban.Dcb.MaterializedView.MySql;

public sealed partial class MySqlMvRegistryStore
{
    private static readonly MvForcedReverseSqlPlan ForcedReverseSql = MvForcedReverseSqlPlan.Create(
        candidateFenceSql: """
            SELECT logical_table FROM sekiban_mv_registry
            WHERE service_id = @ServiceId AND view_name = @ViewName AND view_version = @ViewVersion
            ORDER BY logical_table FOR UPDATE;
            """,
        fenceReturnsRows: true,
        savepointSql: "SAVEPOINT sekiban_mv_forced_reverse;",
        rollbackSavepointSql: "ROLLBACK TO SAVEPOINT sekiban_mv_forced_reverse;",
        releaseSavepointSql: "RELEASE SAVEPOINT sekiban_mv_forced_reverse;");

    protected override MvForcedReverseSqlPlan ForcedReversePlan => ForcedReverseSql;
    protected override MySqlConnection CreateForcedReverseConnection() => new(_connectionString);
    protected override object FormatForcedReverseTimestamp(DateTimeOffset value) => value.UtcDateTime;
}
