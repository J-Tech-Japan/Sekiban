using System.Collections;
using System.Reflection;
using System.Text.Json;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.MaterializedView;

public sealed class MvTableBindings : IMvTableBindings
{
    private readonly Dictionary<string, MvTable> _tables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _logicalToPhysical = new(StringComparer.Ordinal);
    private readonly List<MvTable> _tableList = [];
    private readonly MvOptions _options;
    private readonly string _viewName;
    private readonly int _viewVersion;

    public MvTableBindings(string viewName, int viewVersion, MvOptions options)
    {
        _viewName = viewName;
        _viewVersion = viewVersion;
        _options = options;
    }

    public IReadOnlyDictionary<string, string> LogicalToPhysical => _logicalToPhysical;

    public string GetPhysicalName(string logicalName) => RegisterTable(logicalName).PhysicalName;

    public IReadOnlyList<MvTable> Tables => _tableList;

    public MvTable RegisterTable(string logicalName, string? physicalName = null)
    {
        if (_tables.TryGetValue(logicalName, out var existing))
        {
            return existing;
        }

        var table = new MvTable(
            logicalName,
            physicalName ?? MvPhysicalName.Resolve(_options, _viewName, _viewVersion, logicalName),
            _viewName,
            _viewVersion);
        _tables[logicalName] = table;
        _logicalToPhysical[logicalName] = table.PhysicalName;
        _tableList.Add(table);
        return table;
    }
}

public static class MvParamConverter
{
    public static IReadOnlyList<MvParam> FromObject(object? parameters)
    {
        if (parameters is null)
        {
            return [];
        }

        if (parameters is IReadOnlyList<MvParam> readOnlyParams)
        {
            return readOnlyParams;
        }

        if (parameters is IEnumerable<MvParam> enumerableParams)
        {
            return enumerableParams.ToList();
        }

        if (parameters is IEnumerable<KeyValuePair<string, object?>> keyValuePairs)
        {
            return keyValuePairs.Select(pair => ToParam(pair.Key, pair.Value)).ToList();
        }

        return parameters.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead)
            .Select(property => ToParam(property.Name, property.GetValue(parameters)))
            .ToList();
    }

    public static object? ToClrValue(MvParam param) =>
        param.Kind != MvParamKind.Null && param.ValueJson is null
            ? throw new InvalidOperationException(
                $"Materialized view parameter '{param.Name}' has kind '{param.Kind}' but no serialized value.")
            : param.Kind switch
        {
            MvParamKind.Null => DBNull.Value,
            MvParamKind.String => Deserialize<string>(param.ValueJson),
            MvParamKind.Int32 => Deserialize<int>(param.ValueJson),
            MvParamKind.Int64 => Deserialize<long>(param.ValueJson),
            MvParamKind.Boolean => Deserialize<bool>(param.ValueJson),
            MvParamKind.Guid => Deserialize<Guid>(param.ValueJson),
            MvParamKind.DateTimeOffset => Deserialize<DateTimeOffset>(param.ValueJson),
            MvParamKind.Decimal => Deserialize<decimal>(param.ValueJson),
            MvParamKind.Double => Deserialize<double>(param.ValueJson),
            MvParamKind.Bytes => Deserialize<byte[]>(param.ValueJson),
            MvParamKind.DateTime => Deserialize<DateTime>(param.ValueJson),
            _ => throw new NotSupportedException($"Unsupported materialized view parameter kind '{param.Kind}'.")
        };

    public static string? SerializeScalar(object? value) =>
        value is null or DBNull ? null : JsonSerializer.Serialize(value);

    private static MvParam ToParam(string name, object? value)
    {
        if (value is null)
        {
            return new MvParam(name, MvParamKind.Null, null);
        }

        return value switch
        {
            string stringValue => new MvParam(name, MvParamKind.String, JsonSerializer.Serialize(stringValue)),
            int intValue => new MvParam(name, MvParamKind.Int32, JsonSerializer.Serialize(intValue)),
            long longValue => new MvParam(name, MvParamKind.Int64, JsonSerializer.Serialize(longValue)),
            bool boolValue => new MvParam(name, MvParamKind.Boolean, JsonSerializer.Serialize(boolValue)),
            Guid guidValue => new MvParam(name, MvParamKind.Guid, JsonSerializer.Serialize(guidValue)),
            DateTimeOffset dtoValue => new MvParam(name, MvParamKind.DateTimeOffset, JsonSerializer.Serialize(dtoValue)),
            DateTime dateTimeValue => new MvParam(name, MvParamKind.DateTime, JsonSerializer.Serialize(dateTimeValue)),
            decimal decimalValue => new MvParam(name, MvParamKind.Decimal, JsonSerializer.Serialize(decimalValue)),
            double doubleValue => new MvParam(name, MvParamKind.Double, JsonSerializer.Serialize(doubleValue)),
            float floatValue => new MvParam(name, MvParamKind.Double, JsonSerializer.Serialize((double)floatValue)),
            byte[] bytesValue => new MvParam(name, MvParamKind.Bytes, JsonSerializer.Serialize(bytesValue)),
            _ => throw new NotSupportedException(
                $"Unsupported materialized view SQL parameter type '{value.GetType().FullName}' for '{name}'.")
        };
    }

    private static T Deserialize<T>(string? valueJson)
    {
        if (valueJson is null)
        {
            return default!;
        }

        return JsonSerializer.Deserialize<T>(valueJson)!;
    }
}

public sealed class NativeMvApplyHostFactory : IMvApplyHostFactory
{
    private readonly IEventTypes _eventTypes;
    private readonly Dictionary<(string ViewName, int ViewVersion), IMaterializedViewProjector> _projectors;
    private readonly IReadOnlyList<MvApplyHostRegistration> _registrations;
    private readonly IServiceProvider _services;

    public NativeMvApplyHostFactory(
        IEnumerable<IMaterializedViewProjector> projectors,
        IEventTypes eventTypes,
        IServiceProvider services)
    {
        _eventTypes = eventTypes;
        _services = services;
        _projectors = projectors.ToDictionary(
            projector => (projector.ViewName, projector.ViewVersion),
            projector => projector);
        _registrations = _projectors.Keys
            .Select(key => new MvApplyHostRegistration(key.ViewName, key.ViewVersion))
            .OrderBy(registration => registration.ViewName, StringComparer.Ordinal)
            .ThenBy(registration => registration.ViewVersion)
            .ToList();
    }

    public IReadOnlyList<MvApplyHostRegistration> GetRegistrations() => _registrations;

    public IMvApplyHost Create(string viewName, int viewVersion)
    {
        if (!_projectors.TryGetValue((viewName, viewVersion), out var projector))
        {
            throw new InvalidOperationException($"Materialized view apply host '{viewName}/{viewVersion}' is not registered.");
        }

        return new NativeMvApplyHost(projector, _eventTypes, ResolveDatabaseType());
    }

    private MvDbType ResolveDatabaseType() =>
        (_services.GetService(typeof(IMvStorageInfoProvider)) as IMvStorageInfoProvider)
            ?.GetStorageInfo()
            .DatabaseType
        ?? MvDbType.Postgres;
}

public sealed class NativeMvApplyHost : IMvApplyHost
{
    private readonly MvDbType _databaseType;
    private readonly IEventTypes _eventTypes;
    private readonly IMaterializedViewProjector _projector;
    private readonly List<string> _logicalTables = [];

    public NativeMvApplyHost(
        IMaterializedViewProjector projector,
        IEventTypes eventTypes,
        MvDbType databaseType = MvDbType.Postgres)
    {
        _projector = projector;
        _eventTypes = eventTypes;
        _databaseType = databaseType;
    }

    public string ViewName => _projector.ViewName;
    public int ViewVersion => _projector.ViewVersion;
    public IReadOnlyList<string> LogicalTables => _logicalTables;

    public async Task<IReadOnlyList<MvSqlStatementDto>> InitializeAsync(IMvTableBindings tables, CancellationToken ct)
    {
        var recordingContext = new RecordingMvInitContext(tables, _databaseType);
        await _projector.InitializeAsync(recordingContext, ct).ConfigureAwait(false);

        _logicalTables.Clear();
        _logicalTables.AddRange(tables.LogicalToPhysical.Keys.Order(StringComparer.Ordinal));
        return recordingContext.Statements;
    }

    public async Task<IReadOnlyList<MvSqlStatementDto>> ApplyEventAsync(
        SerializableEvent ev,
        IMvTableBindings tables,
        IMvApplyQueryPort queryPort,
        string sortableUniqueId,
        CancellationToken ct)
    {
        var eventResult = ev.ToEvent(_eventTypes);
        if (!eventResult.IsSuccess)
        {
            throw eventResult.GetException();
        }

        var applyContext = new NativeMvApplyContextAdapter(
            eventResult.GetValue(),
            sortableUniqueId,
            queryPort,
            _databaseType);
        var statements = await _projector.ApplyToViewAsync(eventResult.GetValue(), applyContext, ct).ConfigureAwait(false);
        return statements.Select(statement => new MvSqlStatementDto(statement.Sql, MvParamConverter.FromObject(statement.Parameters))).ToList();
    }

    private sealed class RecordingMvInitContext : IMvInitContext
    {
        private readonly MvDbType _databaseType;
        private readonly IMvTableBindings _bindings;
        private readonly List<MvSqlStatementDto> _statements = [];

        public RecordingMvInitContext(IMvTableBindings bindings, MvDbType databaseType)
        {
            _bindings = bindings;
            _databaseType = databaseType;
        }

        public IReadOnlyList<MvSqlStatementDto> Statements => _statements;
        public MvDbType DatabaseType => _databaseType;
        public System.Data.IDbConnection Connection => throw new NotSupportedException("Native MV init host does not expose raw connections.");

        public MvTable RegisterTable(string logicalName) => _bindings.RegisterTable(logicalName);

        public Task ExecuteAsync(string sql, object? param = null, CancellationToken cancellationToken = default)
        {
            _statements.Add(new MvSqlStatementDto(sql, MvParamConverter.FromObject(param)));
            return Task.CompletedTask;
        }
    }

    private sealed class NativeMvApplyContextAdapter : IMvApplyContext
    {
        private readonly MvDbType _databaseType;
        private readonly IMvApplyQueryPort _queryPort;

        public NativeMvApplyContextAdapter(
            Event currentEvent,
            string sortableUniqueId,
            IMvApplyQueryPort queryPort,
            MvDbType databaseType)
        {
            CurrentEvent = currentEvent;
            CurrentSortableUniqueId = sortableUniqueId;
            _queryPort = queryPort;
            _databaseType = databaseType;
        }

        public MvDbType DatabaseType => _databaseType;
        public System.Data.IDbConnection Connection =>
            _queryPort is IMvApplyDbConnectionPort dbConnectionPort
                ? dbConnectionPort.Connection
                : throw new NotSupportedException("Native MV apply host does not expose raw connections.");
        public System.Data.IDbTransaction Transaction =>
            _queryPort is IMvApplyDbConnectionPort dbConnectionPort
                ? dbConnectionPort.Transaction
                : throw new NotSupportedException("Native MV apply host does not expose raw transactions.");
        public Event CurrentEvent { get; }
        public string CurrentSortableUniqueId { get; }

        public async Task<IMvRow?> QuerySingleOrDefaultRowAsync(string sql, object? param = null, CancellationToken cancellationToken = default)
        {
            var row = await _queryPort.QuerySingleOrDefaultAsync(sql, MvParamConverter.FromObject(param), cancellationToken)
                .ConfigureAwait(false);
            return row is null ? null : new JsonElementMvRow(row.Value);
        }

        public async Task<IMvRowSet> QueryRowsAsync(string sql, object? param = null, CancellationToken cancellationToken = default)
        {
            var rows = await _queryPort.QueryRowsAsync(sql, MvParamConverter.FromObject(param), cancellationToken)
                .ConfigureAwait(false);
            return new JsonElementMvRowSet(rows.Select(row => (IMvRow)new JsonElementMvRow(row)).ToList());
        }

        public async Task<TScalar> ExecuteScalarAsync<TScalar>(string sql, object? param = null, CancellationToken cancellationToken = default)
        {
            var json = await _queryPort.ExecuteScalarJsonAsync(sql, MvParamConverter.FromObject(param), cancellationToken)
                .ConfigureAwait(false);
            if (json is null)
            {
                return default!;
            }

            return JsonSerializer.Deserialize<TScalar>(json)!;
        }

        public MvTable GetDependencyViewTable(string viewName, string logicalTable) =>
            throw new NotSupportedException("Cross-view reads are not supported by the native MV apply host adapter.");

        public MvTable GetDependencyViewTable<TView>(string logicalTable) where TView : IMaterializedViewProjector =>
            throw new NotSupportedException("Cross-view reads are not supported by the native MV apply host adapter.");
    }

    private sealed class JsonElementMvRow : IMvRow
    {
        private readonly IReadOnlyDictionary<string, JsonElement> _values;
        private readonly IReadOnlyList<string> _columnNames;

        public JsonElementMvRow(JsonElement row)
        {
            if (row.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("Materialized view row payload must be a JSON object.");
            }

            _values = row.EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value, StringComparer.OrdinalIgnoreCase);
            _columnNames = _values.Keys.ToList();
        }

        public int ColumnCount => _values.Count;
        public IReadOnlyList<string> ColumnNames => _columnNames;
        public bool IsNull(string columnName) => !_values.TryGetValue(columnName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;
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

    private sealed class JsonElementMvRowSet : IMvRowSet
    {
        private readonly IReadOnlyList<IMvRow> _rows;

        public JsonElementMvRowSet(IReadOnlyList<IMvRow> rows) => _rows = rows;

        public IReadOnlyList<string> ColumnNames => _rows.FirstOrDefault()?.ColumnNames ?? [];
        public int Count => _rows.Count;
        public IMvRow this[int index] => _rows[index];
        public IEnumerator<IMvRow> GetEnumerator() => _rows.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
