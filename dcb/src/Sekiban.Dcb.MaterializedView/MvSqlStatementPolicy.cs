using System.Security.Cryptography;
using System.Text;

namespace Sekiban.Dcb.MaterializedView;

public enum MvSqlStatementPolicyMode
{
    /// <summary>Preserves the additive compatibility behavior, including raw apply-context access.</summary>
    Legacy = 0,

    /// <summary>Gates every SQL surface before provider execution and removes raw apply-context access.</summary>
    Enforced = 1
}

public enum MvSqlStatementPhase
{
    Initialization = 0,
    Apply = 1
}

public enum MvSqlPolicyFailureReason
{
    Denied = 0,
    PolicyUnavailable = 1,
    PolicyEvaluationFailed = 2,
    InvalidDecision = 3
}

public sealed record MvSqlStatementContext(
    string ServiceId,
    string ViewName,
    int ViewVersion,
    MvSqlStatementPhase Phase,
    IReadOnlyList<MvTable> Tables,
    string Sql,
    IReadOnlyList<MvParam> Parameters)
{
    public MvDbType? DatabaseType { get; init; }
    public int StatementIndex { get; init; }
    public int BatchSize { get; init; } = 1;
    public string SqlSha256 => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Sql)));
    public string SqlFingerprint => SqlSha256;
}

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
    MvSqlStatementPhase Phase)
{
    public MvSqlPolicyFailureReason FailureReason { get; init; } = MvSqlPolicyFailureReason.Denied;
    public string? RuleId { get; init; }
    public int StatementIndex { get; init; }
    public int BatchSize { get; init; } = 1;
    public string? SqlSha256 { get; init; }
    public string? SqlFingerprint => SqlSha256;
}

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

internal static class MvSqlPolicyEvaluator
{
    public static async Task AuthorizeAsync(
        IMvSqlStatementPolicy? policy,
        MvSqlStatementContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (policy is null)
        {
            throw CreateRejected(
                context,
                MvSqlPolicyFailureReason.PolicyUnavailable,
                "No enforced materialized-view SQL statement policy is configured.");
        }

        MvSqlPolicyDecision decision;
        try
        {
            decision = await policy.EvaluateAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            throw CreateRejected(
                context,
                MvSqlPolicyFailureReason.PolicyEvaluationFailed,
                "The materialized-view SQL statement policy failed closed while evaluating a statement.");
        }

        if (decision.IsAllowed && !string.IsNullOrWhiteSpace(decision.Reason))
        {
            throw CreateRejected(
                context,
                MvSqlPolicyFailureReason.InvalidDecision,
                "The materialized-view SQL statement policy returned an invalid allow decision.");
        }

        if (!decision.IsAllowed)
        {
            if (string.IsNullOrWhiteSpace(decision.Reason))
            {
                throw CreateRejected(
                    context,
                    MvSqlPolicyFailureReason.InvalidDecision,
                    "The materialized-view SQL statement policy returned an invalid deny decision.");
            }

            throw CreateRejected(context, MvSqlPolicyFailureReason.Denied, decision.Reason);
        }
    }

    private static MvSqlPolicyRejectedException CreateRejected(
        MvSqlStatementContext context,
        MvSqlPolicyFailureReason reason,
        string message) =>
        new(
            new MvSqlPolicyFailure(
                message,
                context.ServiceId,
                context.ViewName,
                context.ViewVersion,
                context.Phase)
            {
                FailureReason = reason,
                StatementIndex = context.StatementIndex,
                BatchSize = context.BatchSize,
                SqlSha256 = context.SqlSha256
            });
}

/// <summary>
///     Enforced-mode query port. It deliberately implements only the query-port contract so the native adapter
///     cannot expose the provider connection or transaction to a projector.
/// </summary>
internal sealed class MvPolicyEnforcingQueryPort : IMvApplyQueryPort
{
    private readonly IMvApplyQueryPort _inner;
    private readonly IMvSqlStatementPolicy? _policy;
    private readonly string _serviceId;
    private readonly string _viewName;
    private readonly int _viewVersion;
    private readonly IReadOnlyList<MvTable> _tables;

    public MvPolicyEnforcingQueryPort(
        IMvApplyQueryPort inner,
        IMvSqlStatementPolicy? policy,
        string serviceId,
        string viewName,
        int viewVersion,
        IReadOnlyList<MvTable> tables)
    {
        _inner = inner;
        _policy = policy;
        _serviceId = serviceId;
        _viewName = viewName;
        _viewVersion = viewVersion;
        _tables = tables;
    }

    public async Task<IReadOnlyList<System.Text.Json.JsonElement>> QueryRowsAsync(
        string sql,
        IReadOnlyList<MvParam> parameters,
        CancellationToken ct)
    {
        await AuthorizeAsync(sql, parameters, ct).ConfigureAwait(false);
        return await _inner.QueryRowsAsync(sql, parameters, ct).ConfigureAwait(false);
    }

    public async Task<System.Text.Json.JsonElement?> QuerySingleOrDefaultAsync(
        string sql,
        IReadOnlyList<MvParam> parameters,
        CancellationToken ct)
    {
        await AuthorizeAsync(sql, parameters, ct).ConfigureAwait(false);
        return await _inner.QuerySingleOrDefaultAsync(sql, parameters, ct).ConfigureAwait(false);
    }

    public async Task<string?> ExecuteScalarJsonAsync(
        string sql,
        IReadOnlyList<MvParam> parameters,
        CancellationToken ct)
    {
        await AuthorizeAsync(sql, parameters, ct).ConfigureAwait(false);
        return await _inner.ExecuteScalarJsonAsync(sql, parameters, ct).ConfigureAwait(false);
    }

    internal MvSqlPolicyRejectedException RejectRawAccess(string surface) =>
        new(
            new MvSqlPolicyFailure(
                $"The enforced materialized-view SQL policy does not expose a {surface} to projectors.",
                _serviceId,
                _viewName,
                _viewVersion,
                MvSqlStatementPhase.Apply));

    private Task AuthorizeAsync(string sql, IReadOnlyList<MvParam> parameters, CancellationToken cancellationToken) =>
        MvSqlPolicyEvaluator.AuthorizeAsync(
            _policy,
            new MvSqlStatementContext(
                _serviceId,
                _viewName,
                _viewVersion,
                MvSqlStatementPhase.Apply,
                _tables,
                sql,
                MetadataOnly(parameters)),
            cancellationToken);

    private static IReadOnlyList<MvParam> MetadataOnly(IReadOnlyList<MvParam> parameters) =>
        parameters.Select(parameter => parameter with { ValueJson = null }).ToList();
}
