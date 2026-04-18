using System.Data;
using System.Text.Json;
using Dapper;

namespace Sekiban.Dcb.MaterializedView.SqlServer;

internal static class SqlServerMvValueAdapter
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

internal sealed class SqlServerMvApplyQueryPort : IMvApplyDbConnectionPort
{
    private readonly IDbConnection _connection;
    private readonly IDbTransaction _transaction;

    public SqlServerMvApplyQueryPort(IDbConnection connection, IDbTransaction transaction)
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
            .Select(row => (JsonElement)JsonSerializer.SerializeToElement(SqlServerMvValueAdapter.ToDictionary(row)))
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
            : JsonSerializer.SerializeToElement(SqlServerMvValueAdapter.ToDictionary(row));
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
