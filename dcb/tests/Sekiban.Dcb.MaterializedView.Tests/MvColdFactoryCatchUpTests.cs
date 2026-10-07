using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.ColdEvents;
using Sekiban.Dcb.ColdEvents.Tests;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.Sqlite;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.Tests;

#pragma warning disable CS0618
public sealed class MvColdFactoryCatchUpTests
{
    private const string Service = "cold-view";

    [Fact]
    public async Task New_generation_reads_cold_then_hot_through_decorated_factory_and_equals_hot_only_build()
    {
        var types = new SimpleEventTypes();
        types.RegisterEventType<Payload>();
        var events = Enumerable.Range(1, 25).Select(n => new Event(new Payload(n),
            SortableUniqueId.Generate(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(n), Guid.Empty),
            nameof(Payload), Guid.NewGuid(), new EventMetadata("test", "test", "test"), []).ToSerializableEvent(types)).ToArray();
        var hotOnlyFactory = new InMemoryEventStoreFactory(types);
        var hybridInner = new InMemoryEventStoreFactory(types);
        Assert.True((await hotOnlyFactory.CreateForService(Service).WriteSerializableEventsAsync(events)).IsSuccess);
        // Deliberately omit the cold prefix from this backend: a bare factory cannot pass the comparison.
        Assert.True((await hybridInner.CreateForService(Service).WriteSerializableEventsAsync(events.Skip(20))).IsSuccess);
        var storage = new InMemoryColdObjectStorage();
        var data = JsonlSegmentWriter.Write(events.Take(20).ToArray());
        const string path = "segments/cold-view/history.jsonl";
        await storage.PutAsync(path, data, null, default);
        var manifest = new ColdManifest(Service, "v1", events[19].SortableUniqueIdValue,
            [new(path, events[0].SortableUniqueIdValue, events[19].SortableUniqueIdValue, 20, data.Length, "test", DateTimeOffset.UnixEpoch)], DateTimeOffset.UnixEpoch);
        await storage.PutAsync(ColdStoragePaths.ManifestPath(Service), JsonSerializer.SerializeToUtf8Bytes(manifest,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), null, default);
        var services = new ServiceCollection();
        services.AddSingleton<IEventStore>(hybridInner.CreateForService(Service));
        services.AddSingleton<IEventStoreFactory>(hybridInner);
        services.AddSingleton<IColdObjectStorage>(storage);
        services.AddSingleton<IColdSegmentFormatHandler>(new JsonlColdSegmentFormatHandler());
        services.AddSingleton<IServiceIdProvider>(new FixedServiceIdProvider(Service));
        services.AddSingleton<IOptions<ColdEventStoreOptions>>(Options.Create(new ColdEventStoreOptions { Enabled = true }));
        services.AddSingleton<ILogger<HybridEventStore>>(NullLogger<HybridEventStore>.Instance);
        services.AddSekibanDcbColdEventHybridRead();
        using var provider = services.BuildServiceProvider();
        var hybridFactory = provider.GetRequiredService<IEventStoreFactory>();
        Assert.IsType<HybridEventStore>(hybridFactory.CreateForService(Service));
        var hotBuild = await Build(hotOnlyFactory, events[^1].SortableUniqueIdValue);
        var coldBuild = await Build(hybridFactory, events[^1].SortableUniqueIdValue);
        Assert.Equal(hotBuild, coldBuild);
        Assert.Equal(Enumerable.Range(1, 25), coldBuild);
    }

    private static async Task<int[]> Build(IEventStoreFactory factory, string target)
    {
        var path = Path.Combine(Path.GetTempPath(), $"sekiban-mv-cold-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path};Pooling=False";
        try
        {
            var registry = new SqliteMvRegistryStore(connectionString);
            var executor = new SqliteMvExecutor(factory, registry,
                Options.Create(new MvOptions { ServiceId = Service, BatchSize = 3, SafeWindowMs = 0 }),
                NullLogger<SqliteMvExecutor>.Instance, connectionString);
            var host = new Host();
            await executor.InitializeAsync(host, Service);
            var initial = Assert.Single(await registry.GetEntriesAsync(Service, host.ViewName, host.ViewVersion));
            Assert.False(initial.CurrentCheckpointTruth.IsKnown);
            var caughtUp = false;
            for (var i = 0; i < 20; i++)
            {
                var result = await executor.CatchUpOnceAsync(host, Service);
                if (result.Outcome == MvCatchUpOutcome.Empty) { caughtUp = true; break; }
                Assert.Equal(MvCatchUpOutcome.Progressed, result.Outcome);
            }
            Assert.True(caughtUp);
            var entry = Assert.Single(await registry.GetEntriesAsync(Service, host.ViewName, host.ViewVersion));
            Assert.Equal(target, entry.CurrentCheckpointTruth.PositionValue);
            Assert.Equal(target, entry.TargetCheckpointTruth.PositionValue);
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            return (await connection.QueryAsync<int>($"SELECT value FROM {entry.PhysicalTable} ORDER BY value")).ToArray();
        }
        finally { File.Delete(path); }
    }

    private sealed record Payload(int Value) : IEventPayload;
    private sealed class Host : IMvApplyHost
    {
        public string ViewName => "ColdFactory";
        public int ViewVersion => 1;
        public IReadOnlyList<string> LogicalTables => ["values"];
        public Task<IReadOnlyList<MvSqlStatementDto>> InitializeAsync(IMvTableBindings tables, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MvSqlStatementDto>>([
                new($"CREATE TABLE {tables.GetPhysicalName("values")} (value INTEGER PRIMARY KEY)", [])]);
        public Task<IReadOnlyList<MvSqlStatementDto>> ApplyEventAsync(SerializableEvent ev, IMvTableBindings tables,
            IMvApplyQueryPort queryPort, string sortableUniqueId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MvSqlStatementDto>>([
                new($"INSERT INTO {tables.GetPhysicalName("values")} (value) VALUES (@Value)",
                    [new("Value", MvParamKind.Int32, JsonSerializer.Serialize(JsonSerializer.Deserialize<Payload>(ev.Payload)!.Value))])]);
    }
}
#pragma warning restore CS0618
