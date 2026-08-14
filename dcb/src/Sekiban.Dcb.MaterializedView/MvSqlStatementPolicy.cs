namespace Sekiban.Dcb.MaterializedView;

public enum MvSqlStatementPhase
{
    Initialization = 0,
    Apply = 1
}

public sealed record MvSqlStatementContext(
    string ServiceId,
    string ViewName,
    int ViewVersion,
    MvSqlStatementPhase Phase,
    IReadOnlyList<MvTable> Tables,
    string Sql,
    IReadOnlyList<MvParam> Parameters);

public readonly record struct MvSqlPolicyDecision(bool IsAllowed, string? Reason)
{
    public static MvSqlPolicyDecision Allow() => new(true, null);

    public static MvSqlPolicyDecision Reject(string reason) =>
        new(false, string.IsNullOrWhiteSpace(reason) ? "The host SQL statement policy rejected the statement." : reason);
}

public interface IMvSqlStatementPolicy
{
    ValueTask<MvSqlPolicyDecision> EvaluateAsync(
        MvSqlStatementContext context,
        CancellationToken cancellationToken = default);
}

public sealed class MvAllowAllSqlStatementPolicy : IMvSqlStatementPolicy
{
    public static MvAllowAllSqlStatementPolicy Instance { get; } = new();

    private MvAllowAllSqlStatementPolicy()
    {
    }

    public ValueTask<MvSqlPolicyDecision> EvaluateAsync(
        MvSqlStatementContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(MvSqlPolicyDecision.Allow());
}

public sealed record MvSqlPolicyFailure(
    string Reason,
    string ServiceId,
    string ViewName,
    int ViewVersion,
    MvSqlStatementPhase Phase);

public sealed class MvSqlPolicyRejectedException : InvalidOperationException
{
    public MvSqlPolicyRejectedException(MvSqlPolicyFailure failure)
        : base(
            $"The materialized-view SQL statement policy rejected a {failure.Phase} statement for " +
            $"service '{failure.ServiceId}', view '{failure.ViewName}/{failure.ViewVersion}': {failure.Reason}")
    {
        Failure = failure;
    }

    public MvSqlPolicyFailure Failure { get; }
}
