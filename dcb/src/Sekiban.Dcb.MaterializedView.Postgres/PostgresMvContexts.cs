using System.Data;
using Dapper;
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
        return row is null ? null : new PostgresMvRow(ToDictionary(row));
    }

    public async Task<IMvRowSet> QueryRowsAsync(string sql, object? param = null, CancellationToken cancellationToken = default)
    {
        var rows = await Connection.QueryAsync(
            new CommandDefinition(sql, param, Transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return new PostgresMvRowSet(rows.Select(row => (IMvRow)new PostgresMvRow(ToDictionary(row))).ToList());
    }

    public Task<TScalar> ExecuteScalarAsync<TScalar>(string sql, object? param = null, CancellationToken cancellationToken = default) =>
        Connection.QuerySingleAsync<TScalar>(
            new CommandDefinition(sql, param, Transaction, cancellationToken: cancellationToken));

    public MvTable GetDependencyViewTable(string viewName, string logicalTable) =>
        throw new NotSupportedException("Cross-view reads are out of scope for the materialized view PoC and are planned for a later phase.");

    public MvTable GetDependencyViewTable<TView>(string logicalTable) where TView : IMaterializedViewProjector =>
        throw new NotSupportedException("Cross-view reads are out of scope for the materialized view PoC and are planned for a later phase.");

    private static IReadOnlyDictionary<string, object?> ToDictionary(object row)
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
            return nonNullableDictionary.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.OrdinalIgnoreCase);
        }

        return row.GetType()
            .GetProperties()
            .ToDictionary(property => property.Name, property => property.GetValue(row), StringComparer.OrdinalIgnoreCase);
    }
}
