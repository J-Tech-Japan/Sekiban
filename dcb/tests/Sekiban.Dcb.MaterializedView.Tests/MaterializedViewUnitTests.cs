using Dcb.Domain.WithoutResult;
using Dcb.Domain.WithoutResult.MaterializedViews;
using Dcb.Domain.WithoutResult.Order;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.Tags;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.Tests;

public class MaterializedViewUnitTests
{
    [Fact]
    public void PhysicalNameResolver_SanitizesAndValidates()
    {
        Assert.Equal("order_summary", MvPhysicalName.SanitizeSegment("Order Summary"));
        Assert.Equal("order_123", MvPhysicalName.SanitizeSegment("Order_日本 123"));
        Assert.Throws<ArgumentException>(() => MvPhysicalName.ValidateIdentifier("bad-name"));
        Assert.Throws<ArgumentException>(() => MvPhysicalName.ValidateIdentifier("OrderSummary"));
        Assert.Throws<ArgumentException>(() => MvPhysicalName.ValidateIdentifier("注文"));
        Assert.Throws<ArgumentException>(() => MvPhysicalName.ValidateIdentifier(new string('a', 64)));
        Assert.Equal(
            "sekiban_mv_ordersummary_v1_items",
            MvPhysicalName.Resolve(new MvOptions(), "OrderSummary", 1, "items"));
    }

    [Fact]
    public void MvRowMapper_MapsRecordAndPoco()
    {
        var row = new FakeMvRow(new Dictionary<string, object?>
        {
            ["id"] = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ["name"] = "sekiban",
            ["created_at"] = new DateTimeOffset(2026, 4, 15, 12, 0, 0, TimeSpan.Zero),
            ["count"] = 3
        });

        var record = MvRowMapper<RecordTarget>.MapFrom(row);
        var poco = MvRowMapper<PocoTarget>.MapFrom(row);

        Assert.Equal("sekiban", record.Name);
        Assert.Equal(3, poco.Count);
        Assert.Equal(record.CreatedAt, poco.CreatedAt);

        var dualCtor = MvRowMapper<DualConstructorTarget>.MapFrom(row);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), dualCtor.Id);
        Assert.Equal("sekiban", dualCtor.Name);
    }

    [Fact]
    public async Task OrderSummaryMvV1_ReturnsExpectedStatements()
    {
        var view = new OrderSummaryMvV1();
        await view.InitializeAsync(new FakeInitContext());
        var ctx = new FakeApplyContext();

        var createdStatements = await view.ApplyToViewAsync(
            new Event(
                new OrderCreated(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), DateTimeOffset.UtcNow),
                "100",
                nameof(OrderCreated),
                Guid.NewGuid(),
                new EventMetadata("cause", "corr", "tester"),
                []),
            ctx);

        Assert.Single(createdStatements);
        Assert.Contains("INSERT INTO sekiban_mv_ordersummary_v1_orders", createdStatements[0].Sql);
        Assert.Equal("999", ReadParameter(createdStatements[0].Parameters, "SortableUniqueId"));

        ctx.ScalarResults["SELECT COUNT(*) FROM sekiban_mv_ordersummary_v1_orders WHERE id = @Id"] = 1;

        var itemStatements = await view.ApplyToViewAsync(
            new Event(
                new OrderItemAdded(
                    Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                    "Keyboard",
                    2,
                    12m,
                    DateTimeOffset.UtcNow),
                "101",
                nameof(OrderItemAdded),
                Guid.NewGuid(),
                new EventMetadata("cause", "corr", "tester"),
                []),
            ctx);

        Assert.Equal(2, itemStatements.Count);
        Assert.Contains("sekiban_mv_ordersummary_v1_items", itemStatements[0].Sql);
        Assert.Contains("UPDATE sekiban_mv_ordersummary_v1_orders", itemStatements[1].Sql);
        Assert.Equal(24m, ReadParameter(itemStatements[1].Parameters, "Delta"));

        ctx.ScalarResults["SELECT COUNT(*) FROM sekiban_mv_ordersummary_v1_orders WHERE id = @Id"] = 0;
        var noOrderStatements = await view.ApplyToViewAsync(
            new Event(
                new OrderItemAdded(
                    Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                    "Monitor",
                    1,
                    50m,
                    DateTimeOffset.UtcNow),
                "102",
                nameof(OrderItemAdded),
                Guid.NewGuid(),
                new EventMetadata("cause", "corr", "tester"),
                []),
            ctx);
        Assert.Empty(noOrderStatements);

        var cancelledStatements = await view.ApplyToViewAsync(
            new Event(
                new OrderCancelled(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), DateTimeOffset.UtcNow),
                "103",
                nameof(OrderCancelled),
                Guid.NewGuid(),
                new EventMetadata("cause", "corr", "tester"),
                []),
            ctx);

        Assert.Single(cancelledStatements);
        Assert.Contains("SET status = @Status", cancelledStatements[0].Sql);

        var unknownStatements = await view.ApplyToViewAsync(
            new Event(
                new UnknownEvent(),
                "104",
                nameof(UnknownEvent),
                Guid.NewGuid(),
                new EventMetadata("cause", "corr", "tester"),
                []),
            ctx);
        Assert.Empty(unknownStatements);
    }

    [Fact]
    public async Task CatchUpWorker_UsesPollDelayWhenNothingProcessed()
    {
        var executor = new FakeMvExecutor();
        var hostFactory = new FakeApplyHostFactory(new FakeApplyHost("Fake", 1));
        using var worker = new MvCatchUpWorker(
            hostFactory,
            executor,
            Options.Create(new MvOptions { PollInterval = TimeSpan.FromMilliseconds(10) }),
            NullLogger<MvCatchUpWorker>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await worker.StartAsync(cts.Token);
        await Task.Delay(30, cts.Token);
        await worker.StopAsync(CancellationToken.None);

        Assert.True(executor.InitializeCalls >= 1);
        Assert.True(executor.CatchUpCalls >= 1);
    }

    [Fact]
    public async Task NativeMvApplyHost_AdaptsExistingProjector_ToTypedStatements()
    {
        var domainTypes = DomainType.GetDomainTypes();
        var host = new NativeMvApplyHost(new OrderSummaryMvV1(), domainTypes.EventTypes);
        var bindings = new MvTableBindings("OrderSummary", 1, new MvOptions());

        var initStatements = await host.InitializeAsync(bindings, CancellationToken.None);

        Assert.NotEmpty(initStatements);
        Assert.Contains(bindings.Tables, table => table.LogicalName == "orders");
        Assert.Contains(bindings.Tables, table => table.LogicalName == "items");

        var evt = new Event(
            new OrderCreated(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), DateTimeOffset.UtcNow),
            "100",
            nameof(OrderCreated),
            Guid.NewGuid(),
            new EventMetadata("cause", "corr", "tester"),
            []);
        var serializable = evt.ToSerializableEvent(domainTypes.EventTypes);
        var statements = await host.ApplyEventAsync(
            serializable,
            bindings,
            new FakeApplyQueryPort(),
            "999",
            CancellationToken.None);

        Assert.Single(statements);
        Assert.Contains("INSERT INTO sekiban_mv_ordersummary_v1_orders", statements[0].Sql);
        Assert.Contains(statements[0].Parameters, parameter => parameter.Name == "SortableUniqueId" && parameter.Kind == MvParamKind.String);
    }

    [Fact]
    public void MvParamConverter_RoundTripsTypedParameters()
    {
        var when = new DateTimeOffset(2026, 4, 17, 10, 0, 0, TimeSpan.Zero);
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var parameters = MvParamConverter.FromObject(new
        {
            Name = "sekiban",
            Count = 3,
            Amount = 12.5m,
            Active = true,
            When = when,
            Id = id
        });

        Assert.Equal(MvParamKind.String, parameters.Single(parameter => parameter.Name == "Name").Kind);
        Assert.Equal(MvParamKind.Int32, parameters.Single(parameter => parameter.Name == "Count").Kind);
        Assert.Equal(MvParamKind.Decimal, parameters.Single(parameter => parameter.Name == "Amount").Kind);
        Assert.Equal(MvParamKind.Boolean, parameters.Single(parameter => parameter.Name == "Active").Kind);
        Assert.Equal(MvParamKind.DateTimeOffset, parameters.Single(parameter => parameter.Name == "When").Kind);
        Assert.Equal(MvParamKind.Guid, parameters.Single(parameter => parameter.Name == "Id").Kind);
        Assert.Equal(3, MvParamConverter.ToClrValue(parameters.Single(parameter => parameter.Name == "Count")));
        Assert.Equal(when, MvParamConverter.ToClrValue(parameters.Single(parameter => parameter.Name == "When")));
        Assert.Equal(id, MvParamConverter.ToClrValue(parameters.Single(parameter => parameter.Name == "Id")));
    }

    [Fact]
    public void MvParamConverter_RejectsNullPayloadForNonNullKind()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => MvParamConverter.ToClrValue(new MvParam("Name", MvParamKind.String, null)));

        Assert.Contains("Name", exception.Message);
    }

    [Fact]
    public void SerializableTagState_ResolvedPayloadName_PrefersActualPayloadName()
    {
        var state = new SerializableTagState(
            [],
            1,
            "0001",
            "group",
            "content",
            "projector",
            "UnionPayload",
            "v1",
            "ActualPayload");

        Assert.Equal("ActualPayload", state.ResolvedPayloadName);
    }

    [Fact]
    public void MvStorageInfoProvider_ReturnsConfiguredStorageInfo()
    {
        var provider = new MvStorageInfoProvider(new MvStorageInfo(MvDbType.Postgres, "Host=test;Database=mv;"));

        var resolved = provider.GetStorageInfo();

        Assert.Equal(MvDbType.Postgres, resolved.DatabaseType);
        Assert.Equal("Host=test;Database=mv;", resolved.ConnectionString);
    }

    private sealed record RecordTarget(
        [property: MvColumn("id")] Guid Id,
        [property: MvColumn("name")] string Name,
        [property: MvColumn("created_at")] DateTimeOffset CreatedAt);

    private sealed class PocoTarget
    {
        [MvColumn("id")] public Guid Id { get; init; }
        [MvColumn("name")] public string Name { get; init; } = string.Empty;
        [MvColumn("created_at")] public DateTimeOffset CreatedAt { get; init; }
        [MvColumn("count")] public int Count { get; init; }
    }

    private sealed class DualConstructorTarget
    {
        public DualConstructorTarget()
        {
            Id = Guid.Empty;
            Name = string.Empty;
        }

        public DualConstructorTarget(Guid id, string name)
        {
            Id = id;
            Name = name;
        }

        [MvColumn("id")] public Guid Id { get; }
        [MvColumn("name")] public string Name { get; }
    }

    private sealed class FakeMvRow : IMvRow
    {
        private readonly IReadOnlyDictionary<string, object?> _values;

        public FakeMvRow(IReadOnlyDictionary<string, object?> values)
        {
            _values = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);
        }

        public int ColumnCount => _values.Count;
        public IReadOnlyList<string> ColumnNames => _values.Keys.ToList();
        public bool IsNull(string columnName) => !_values.TryGetValue(columnName, out var value) || value is null;
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
        public T GetAs<T>(string columnName) => MvRowValueConverter.ConvertValue<T>(_values[columnName]);
        public string ToJson() => "{}";
    }

    private sealed class FakeInitContext : IMvInitContext
    {
        public MvDbType DatabaseType => MvDbType.Postgres;
        public System.Data.IDbConnection Connection => throw new NotSupportedException();
        public MvTable RegisterTable(string logicalName) => new(logicalName, $"sekiban_mv_ordersummary_v1_{logicalName}", "OrderSummary", 1);
        public Task ExecuteAsync(string sql, object? param = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeApplyContext : IMvApplyContext
    {
        public Dictionary<string, IMvRow> SingleResults { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, object> ScalarResults { get; } = new(StringComparer.OrdinalIgnoreCase);
        public MvDbType DatabaseType => MvDbType.Postgres;
        public System.Data.IDbConnection Connection => throw new NotSupportedException();
        public System.Data.IDbTransaction Transaction => throw new NotSupportedException();
        public Event CurrentEvent => throw new NotSupportedException();
        public string CurrentSortableUniqueId => "999";

        public Task<IMvRow?> QuerySingleOrDefaultRowAsync(string sql, object? param = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(SingleResults.TryGetValue(sql, out var row) ? row : null);

        public Task<IMvRowSet> QueryRowsAsync(string sql, object? param = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IMvRowSet>(new FakeMvRowSet([]));

        public Task<TScalar> ExecuteScalarAsync<TScalar>(string sql, object? param = null, CancellationToken cancellationToken = default) =>
            Task.FromResult((TScalar)ScalarResults[sql]);

        public MvTable GetDependencyViewTable(string viewName, string logicalTable) => throw new NotSupportedException();
        public MvTable GetDependencyViewTable<TView>(string logicalTable) where TView : IMaterializedViewProjector => throw new NotSupportedException();
    }

    private sealed class FakeMvRowSet(IReadOnlyList<IMvRow> rows) : IMvRowSet
    {
        public IReadOnlyList<string> ColumnNames => rows.FirstOrDefault()?.ColumnNames ?? [];
        public int Count => rows.Count;
        public IMvRow this[int index] => rows[index];
        public IEnumerator<IMvRow> GetEnumerator() => rows.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class FakeMvExecutor : IMvExecutor
    {
        public int InitializeCalls { get; private set; }
        public int CatchUpCalls { get; private set; }

        public Task InitializeAsync(
            IMvApplyHost host,
            string? serviceId = null,
            CancellationToken cancellationToken = default)
        {
            InitializeCalls++;
            return Task.CompletedTask;
        }

        public Task<MvCatchUpResult> CatchUpOnceAsync(
            IMvApplyHost host,
            string? serviceId = null,
            CancellationToken cancellationToken = default)
        {
            CatchUpCalls++;
            return Task.FromResult(new MvCatchUpResult(0, false));
        }

        public Task<int> ApplySerializableEventsAsync(
            IMvApplyHost host,
            IReadOnlyList<SerializableEvent> events,
            string? serviceId = null,
            CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FakeApplyHostFactory : IMvApplyHostFactory
    {
        private readonly IMvApplyHost _host;

        public FakeApplyHostFactory(IMvApplyHost host) => _host = host;

        public IReadOnlyList<MvApplyHostRegistration> GetRegistrations() => [new(_host.ViewName, _host.ViewVersion)];

        public IMvApplyHost Create(string viewName, int viewVersion)
        {
            if (_host.ViewName != viewName || _host.ViewVersion != viewVersion)
            {
                throw new InvalidOperationException("Unexpected host lookup.");
            }

            return _host;
        }
    }

    private sealed class FakeApplyHost : IMvApplyHost
    {
        public FakeApplyHost(string viewName, int viewVersion)
        {
            ViewName = viewName;
            ViewVersion = viewVersion;
        }

        public string ViewName { get; }
        public int ViewVersion { get; }
        public IReadOnlyList<string> LogicalTables => ["main"];

        public Task<IReadOnlyList<MvSqlStatementDto>> InitializeAsync(IMvTableBindings tables, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MvSqlStatementDto>>([]);

        public Task<IReadOnlyList<MvSqlStatementDto>> ApplyEventAsync(
            SerializableEvent ev,
            IMvTableBindings tables,
            IMvApplyQueryPort queryPort,
            string sortableUniqueId,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MvSqlStatementDto>>([]);
    }

    private sealed class FakeApplyQueryPort : IMvApplyQueryPort
    {
        public Task<IReadOnlyList<System.Text.Json.JsonElement>> QueryRowsAsync(
            string sql,
            IReadOnlyList<MvParam> parameters,
            CancellationToken ct) => Task.FromResult<IReadOnlyList<System.Text.Json.JsonElement>>([]);

        public Task<System.Text.Json.JsonElement?> QuerySingleOrDefaultAsync(
            string sql,
            IReadOnlyList<MvParam> parameters,
            CancellationToken ct) => Task.FromResult<System.Text.Json.JsonElement?>(null);

        public Task<string?> ExecuteScalarJsonAsync(
            string sql,
            IReadOnlyList<MvParam> parameters,
            CancellationToken ct) => Task.FromResult<string?>(null);
    }

    private sealed class FakeProjector : IMaterializedViewProjector
    {
        public string ViewName => "Fake";
        public int ViewVersion => 1;
        public Task InitializeAsync(IMvInitContext ctx, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<MvSqlStatement>> ApplyToViewAsync(Event ev, IMvApplyContext ctx, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MvSqlStatement>>([]);
    }

    private sealed record UnknownEvent : IEventPayload;

    private static object? ReadParameter(object? parameterBag, string name) =>
        parameterBag?.GetType().GetProperty(name)?.GetValue(parameterBag);
}
