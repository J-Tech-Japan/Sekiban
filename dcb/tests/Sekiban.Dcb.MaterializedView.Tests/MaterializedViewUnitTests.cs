using Dcb.Domain.WithoutResult.MaterializedViews;
using Dcb.Domain.WithoutResult.Order;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.Tests;

public class MaterializedViewUnitTests
{
    [Fact]
    public void PhysicalNameResolver_SanitizesAndValidates()
    {
        Assert.Equal("order_summary", MvPhysicalName.SanitizeSegment("Order Summary"));
        Assert.Throws<ArgumentException>(() => MvPhysicalName.ValidateIdentifier("bad-name"));
        Assert.Equal(
            "sekiban_mv_ordersummary_v1_items",
            MvPhysicalName.Resolve(new MvWorkerOptions(), "OrderSummary", 1, "items"));
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

        ctx.SingleResults["SELECT * FROM sekiban_mv_ordersummary_v1_orders WHERE id = @Id"] = new FakeMvRow(
            new Dictionary<string, object?>
            {
                ["id"] = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                ["status"] = "Pending",
                ["total"] = 10m,
                ["created_at"] = DateTimeOffset.UtcNow,
                ["_last_sortable_unique_id"] = "090",
                ["_last_applied_at"] = DateTimeOffset.UtcNow
            });

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

        var cancelledStatements = await view.ApplyToViewAsync(
            new Event(
                new OrderCancelled(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), DateTimeOffset.UtcNow),
                "102",
                nameof(OrderCancelled),
                Guid.NewGuid(),
                new EventMetadata("cause", "corr", "tester"),
                []),
            ctx);

        Assert.Single(cancelledStatements);
        Assert.Contains("SET status = @Status", cancelledStatements[0].Sql);
    }

    [Fact]
    public async Task CatchUpWorker_UsesPollDelayWhenNothingProcessed()
    {
        var executor = new FakeMvExecutor();
        var projector = new FakeProjector();
        using var worker = new MvCatchUpWorker(
            [projector],
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
        public MvDbType DatabaseType => MvDbType.Postgres;
        public System.Data.IDbConnection Connection => throw new NotSupportedException();
        public System.Data.IDbTransaction Transaction => throw new NotSupportedException();
        public Event CurrentEvent => throw new NotSupportedException();
        public string CurrentSortableUniqueId => "999";

        public Task<IMvRow?> QuerySingleOrDefaultRowAsync(string sql, object? param = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(SingleResults.TryGetValue(sql, out var row) ? row : null);

        public Task<IMvRowSet> QueryRowsAsync(string sql, object? param = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IMvRowSet>(new FakeMvRowSet([]));

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

        public Task InitializeAsync(IMaterializedViewProjector projector, CancellationToken cancellationToken = default)
        {
            InitializeCalls++;
            return Task.CompletedTask;
        }

        public Task<MvCatchUpResult> CatchUpOnceAsync(IMaterializedViewProjector projector, CancellationToken cancellationToken = default)
        {
            CatchUpCalls++;
            return Task.FromResult(new MvCatchUpResult(0, false));
        }
    }

    private sealed class FakeProjector : IMaterializedViewProjector
    {
        public string ViewName => "Fake";
        public int ViewVersion => 1;
        public Task InitializeAsync(IMvInitContext ctx, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<MvSqlStatement>> ApplyToViewAsync(Event ev, IMvApplyContext ctx, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MvSqlStatement>>([]);
    }
}
