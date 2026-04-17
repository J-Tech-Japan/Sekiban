using System.Data;
using Dapper;
using System.Text.Json;
using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.MaterializedView.Postgres;

internal sealed class PostgresMvInitContext : IMvInitContext
{
    private readonly IDbTransaction _transaction;
    private readonly MvOptions _options;
    private readonly List<MvTable> _registeredTables = [];

    public PostgresMvInitContext(
        IDbConnection connection,
        IDbTransaction transaction,
        string viewName,
        int viewVersion,
        MvOptions options)
    {
        Connection = connection;
        _transaction = transaction;
        _options = options;
        ViewName = viewName;
        ViewVersion = viewVersion;
    }

    public MvDbType DatabaseType => MvDbType.Postgres;
    public IDbConnection Connection { get; }
    public string ViewName { get; }
    public int ViewVersion { get; }
    public IReadOnlyList<MvTable> RegisteredTables => _registeredTables;

    public MvTable RegisterTable(string logicalName)
    {
        var table = new MvTable(
            logicalName,
            MvPhysicalName.Resolve(_options, ViewName, ViewVersion, logicalName),
            ViewName,
            ViewVersion);
        _registeredTables.Add(table);
        return table;
    }

    public Task ExecuteAsync(string sql, object? param = null, CancellationToken cancellationToken = default) =>
        Connection.ExecuteAsync(new CommandDefinition(sql, param, _transaction, cancellationToken: cancellationToken));
}

internal sealed class PostgresMvApplyContext : IMvApplyContext
{
    public PostgresMvApplyContext(IDbConnection connection, IDbTransaction transaction, Event currentEvent, string sortableUniqueId)
    {
        Connection = connection;
        Transaction = transaction;
        CurrentEvent = currentEvent;
        CurrentSortableUniqueId = sortableUniqueId;
    }

    public MvDbType DatabaseType => MvDbType.Postgres;
    public IDbConnection Connection { get; }
    public IDbTransaction Transaction { get; }
    public Event CurrentEvent { get; }
    public string CurrentSortableUniqueId { get; }

    public async Task<IMvRow?> QuerySingleOrDefaultRowAsync(string sql, object? param = null, CancellationToken cancellationToken = default)
    {
        var row = await Connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, param, Transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return row is null ? null : new PostgresMvRow(PostgresMvValueAdapter.ToDictionary(row));
    }

    public async Task<IMvRowSet> QueryRowsAsync(string sql, object? param = null, CancellationToken cancellationToken = default)
    {
        var rows = await Connection.QueryAsync(
            new CommandDefinition(sql, param, Transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return new PostgresMvRowSet(rows.Select(row => (IMvRow)new PostgresMvRow(PostgresMvValueAdapter.ToDictionary(row))).ToList());
    }

    public Task<TScalar> ExecuteScalarAsync<TScalar>(string sql, object? param = null, CancellationToken cancellationToken = default) =>
        Connection.QuerySingleAsync<TScalar>(
            new CommandDefinition(sql, param, Transaction, cancellationToken: cancellationToken));

    public MvTable GetDependencyViewTable(string viewName, string logicalTable) =>
        throw new NotSupportedException("Cross-view reads are out of scope for the materialized view PoC and are planned for a later phase.");

    public MvTable GetDependencyViewTable<TView>(string logicalTable) where TView : IMaterializedViewProjector =>
        throw new NotSupportedException("Cross-view reads are out of scope for the materialized view PoC and are planned for a later phase.");

}

internal static class PostgresMvValueAdapter
{
    public static IReadOnlyDictionary<string, object?> ToDictionary(object row)
    {
        if (row is IReadOnlyDictionary<string, object?> readOnlyDictionary)
        {
            return readOnlyDictionary;
        }

        if (row is IDictionary<string, object?> dictionary)
        {
            return dictionary.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        }

        if (row is IDictionary<string, object> nonNullableDictionary)
        {
            return nonNullableDictionary
                .Select(pair => new KeyValuePair<string, object?>(pair.Key, pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        }

        return row.GetType()
            .GetProperties()
            .ToDictionary(property => property.Name, property => property.GetValue(row), StringComparer.OrdinalIgnoreCase);
    }
}

internal sealed class PostgresMvApplyQueryPort : IMvApplyDbConnectionPort
{
    private readonly IDbConnection _connection;
    private readonly IDbTransaction _transaction;

    public PostgresMvApplyQueryPort(IDbConnection connection, IDbTransaction transaction)
    {
        _connection = connection;
        _transaction = transaction;
    }

    public IDbConnection Connection => _connection;
    public IDbTransaction Transaction => _transaction;

    public async Task<IReadOnlyList<JsonElement>> QueryRowsAsync(
        string sql,
        IReadOnlyList<MvParam> parameters,
        CancellationToken ct)
    {
        var rows = await _connection.QueryAsync(
            new CommandDefinition(
                sql,
                ToDynamicParameters(parameters),
                _transaction,
                cancellationToken: ct)).ConfigureAwait(false);
        return rows
            .Select(row => (JsonElement)JsonSerializer.SerializeToElement(PostgresMvValueAdapter.ToDictionary(row)))
            .ToList();
    }

    public async Task<JsonElement?> QuerySingleOrDefaultAsync(
        string sql,
        IReadOnlyList<MvParam> parameters,
        CancellationToken ct)
    {
        var row = await _connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(
                sql,
                ToDynamicParameters(parameters),
                _transaction,
                cancellationToken: ct)).ConfigureAwait(false);
        return row is null
            ? null
            : JsonSerializer.SerializeToElement(PostgresMvValueAdapter.ToDictionary(row));
    }

    public async Task<string?> ExecuteScalarJsonAsync(
        string sql,
        IReadOnlyList<MvParam> parameters,
        CancellationToken ct)
    {
        var scalar = await _connection.ExecuteScalarAsync(
            new CommandDefinition(
                sql,
                ToDynamicParameters(parameters),
                _transaction,
                cancellationToken: ct)).ConfigureAwait(false);
        return MvParamConverter.SerializeScalar(scalar);
    }

    private static DynamicParameters ToDynamicParameters(IReadOnlyList<MvParam> parameters)
    {
        var dynamicParameters = new DynamicParameters();
        foreach (var parameter in parameters)
        {
            dynamicParameters.Add(parameter.Name, MvParamConverter.ToClrValue(parameter));
        }

        return dynamicParameters;
    }
}
