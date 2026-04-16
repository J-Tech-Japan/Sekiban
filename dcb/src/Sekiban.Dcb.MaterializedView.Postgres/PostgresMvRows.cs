using System.Collections;
using System.Text.Json;

namespace Sekiban.Dcb.MaterializedView.Postgres;

public sealed class PostgresMvRow : IMvRow
{
    private readonly IReadOnlyDictionary<string, object?> _values;
    private readonly IReadOnlyList<string> _columnNames;

    public PostgresMvRow(IReadOnlyDictionary<string, object?> values)
    {
        _values = values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        _columnNames = _values.Keys.ToList();
    }

    public int ColumnCount => _values.Count;
    public IReadOnlyList<string> ColumnNames => _columnNames;

    public bool IsNull(string columnName) => !_values.TryGetValue(columnName, out var value) || value is null || value is DBNull;
    public Guid GetGuid(string columnName) => GetAs<Guid>(columnName);
    public string GetString(string columnName) => GetAs<string>(columnName);
    public int GetInt32(string columnName) => GetAs<int>(columnName);
    public long GetInt64(string columnName) => GetAs<long>(columnName);
    public decimal GetDecimal(string columnName) => GetAs<decimal>(columnName);
    public double GetDouble(string columnName) => GetAs<double>(columnName);
    public bool GetBoolean(string columnName) => GetAs<bool>(columnName);
    public DateTimeOffset GetDateTimeOffset(string columnName) => GetAs<DateTimeOffset>(columnName);
    public byte[] GetBytes(string columnName) => GetAs<byte[]>(columnName);
    public Guid? GetGuidOrNull(string columnName) => GetAs<Guid?>(columnName);
    public string? GetStringOrNull(string columnName) => GetAs<string?>(columnName);
    public int? GetInt32OrNull(string columnName) => GetAs<int?>(columnName);
    public decimal? GetDecimalOrNull(string columnName) => GetAs<decimal?>(columnName);
    public DateTimeOffset? GetDateTimeOffsetOrNull(string columnName) => GetAs<DateTimeOffset?>(columnName);

    public T GetAs<T>(string columnName)
    {
        if (!_values.TryGetValue(columnName, out var value))
        {
            throw new KeyNotFoundException($"Column '{columnName}' does not exist.");
        }

        return MvRowValueConverter.ConvertValue<T>(value);
    }

    public string ToJson() => JsonSerializer.Serialize(_values);
}

public sealed class PostgresMvRowSet : IMvRowSet
{
    private readonly IReadOnlyList<IMvRow> _rows;

    public PostgresMvRowSet(IReadOnlyList<IMvRow> rows)
    {
        _rows = rows;
    }

    public IReadOnlyList<string> ColumnNames => _rows.FirstOrDefault()?.ColumnNames ?? [];
    public int Count => _rows.Count;
    public IMvRow this[int index] => _rows[index];
    public IEnumerator<IMvRow> GetEnumerator() => _rows.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
