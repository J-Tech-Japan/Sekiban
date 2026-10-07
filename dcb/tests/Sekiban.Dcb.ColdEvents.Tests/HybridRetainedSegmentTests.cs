using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ResultBoxes;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.ColdEvents;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Xunit.Abstractions;

namespace Sekiban.Dcb.ColdEvents.Tests;

public class HybridRetainedSegmentTests(ITestOutputHelper output)
{
    private static SerializableEvent Event(int n, string service = "a") => new(
        "{}"u8.ToArray(), Id(n), Guid.NewGuid(), new EventMetadata("test", "test", "test"), [], service);
    private static string Id(int n) => SortableUniqueId.Generate(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(n), Guid.Empty);
    private static SortableUniqueId? Since(int? n) => n.HasValue ? new(Id(n.Value)) : null;
    private static async Task<SerializableEvent[]> Read(IEventStore store, int? since, int? count)
    {
        var result = await store.ReadAllSerializableEventsAsync(Since(since), count);
        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.GetException().ToString());
        return result.GetValue().ToArray();
    }

    [Fact]
    public async Task Counting_100000_events_in_batches_of_100()
    {
        using var fixture = new Fixture();
        var events = Enumerable.Range(1, 100_000).Select(n => Event(n)).ToArray();
        await fixture.Seed("a", events);
        var store = fixture.Singleton;
        var actual = new List<Guid>();
        for (var since = 0; since < 100_000; since += 100)
            actual.AddRange((await Read(store, since == 0 ? null : since, 100)).Select(e => e.Id));
        Assert.Equal(events.Select(e => e.Id), actual);
        output.WriteLine($"100000/100: opens={fixture.Storage.Opens}, manifests={fixture.Storage.Manifests}");
        Assert.Equal(1, fixture.Storage.Opens);
        Assert.InRange(fixture.Storage.Manifests, 1, 2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(12)]
    public async Task Fill_spanning_segments_uses_segment_local_loadedFrom(int? nextSince)
    {
        using var fixture = new Fixture();
        await fixture.Seed("a", Enumerable.Range(1, 4).Select(n => Event(n)).ToArray(), Enumerable.Range(5, 8).Select(n => Event(n)).ToArray());
        await Read(fixture.Singleton, 1, 5); // Returns 2..6: loadedFrom must be 6, not 1.
        await fixture.Compare(fixture.Singleton, nextSince, 3);
    }

    [Fact]
    public async Task Retry_earlier_reader_other_segment_and_interleaved_readers_equal_uncached()
    {
        using var f = new Fixture();
        await f.Seed("a", Enumerable.Range(1, 10).Select(n => Event(n)).ToArray(), Enumerable.Range(11, 10).Select(n => Event(n)).ToArray());
        foreach (var (since, count) in new (int?, int)[] { (2, 3), (2, 3), (null, 3), (7, 2), (3, 2), (12, 2), (4, 2), (13, 2), (14, 4), (1, 4) })
            await f.Compare(f.Singleton, since, count);
    }

    [Fact]
    public async Task Two_readers_near_same_position_both_hit_without_trimming()
    {
        using var f = new Fixture();
        await f.Seed("a", Enumerable.Range(1, 20).Select(n => Event(n)).ToArray());
        await Read(f.Singleton, null, 3);
        var opens = f.Storage.Opens;
        await f.Compare(f.Singleton, 5, 3);
        await f.Compare(f.Singleton, 3, 3);
        Assert.Equal(opens, f.Storage.Opens);
    }

    [Theory]
    [InlineData("jsonl")]
    [InlineData("sqlite")]
    [InlineData("duckdb")]
    public async Task Batched_reads_equal_full_read_across_segments_and_hot_tail(string format)
    {
        IColdSegmentFormatHandler handler = format switch {
            "sqlite" => new SqliteColdSegmentFormatHandler(), "duckdb" => new DuckDbColdSegmentFormatHandler(), _ => new JsonlColdSegmentFormatHandler() };
        using var f = new Fixture(handler: handler, hot: Enumerable.Range(21, 5).Select(n => Event(n)).ToArray());
        await f.Seed("a", Enumerable.Range(1, 10).Select(n => Event(n)).ToArray(), Enumerable.Range(11, 10).Select(n => Event(n)).ToArray());
        var full = await Read(f.Uncached("a"), null, null);
        var batches = new List<SerializableEvent>();
        SortableUniqueId? since = null;
        while (true)
        {
            var batch = (await f.Singleton.ReadAllSerializableEventsAsync(since, 3)).GetValue().ToArray();
            if (batch.Length == 0) break;
            batches.AddRange(batch);
            since = new(batch[^1].SortableUniqueIdValue);
        }
        Assert.Equal(full.Select(e => e.Id), batches.Select(e => e.Id));
        Assert.Equal(2, f.Storage.Opens);
        Assert.True(f.Storage.Manifests < 10);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Equal_ids_at_loadedFrom_and_batch_boundary_equal_uncached(bool sorted)
    {
        using var f = new Fixture();
        var ids = sorted
            ? new[] { 1, 2, 2, 2, 3, 4, 4, 4, 5, 6, 7, 8 }
            : new[] { 1, 2, 2, 2, 4, 3, 4, 4, 5, 6, 7, 8 };
        await f.Seed("a", ids.Select(n => Event(n)).ToArray());
        await Read(f.Singleton, null, 2);
        var opens = f.Storage.Opens;
        foreach (var since in new[] { 2, 3, 4, 2, 5 }) await f.Compare(f.Singleton, since, 2);
        Assert.Equal(opens, f.Storage.Opens);
        foreach (var count in new int?[] { 1, 2, 3, 6, 20, null })
            foreach (var since in new[] { 2, 3, 4, 5, 6, 7, 8 })
            {
                await Read(f.Singleton, null, 2); // Refill so each case exercises retained selection.
                await f.Compare(f.Singleton, since, count);
            }
    }

    [Fact]
    public async Task Unsorted_returned_prefix_cannot_hide_events_on_later_reads()
    {
        using var f = new Fixture();
        await f.Seed("a", new[] { 1, 5, 2, 3, 4, 6, 7, 8 }.Select(n => Event(n)).ToArray());
        await Read(f.Singleton, null, 3);
        await f.Compare(f.Singleton, 2, 3);
    }

    [Fact]
    public async Task Factory_shares_holder_per_service_but_never_between_services()
    {
        using var f = new Fixture();
        foreach (var s in new[] { "a", "b" }) await f.Seed(s, Enumerable.Range(1, 10).Select(n => Event(n, s)).ToArray());
        Assert.Same(f.Factory, f.Factory);
        Assert.IsType<HybridEventStore>(f.Factory.CreateForService("a"));
        await Read(f.Factory.CreateForService("a"), null, 2);
        await Read(f.Factory.CreateForService("b"), null, 2);
        var opens = f.Storage.Opens;
        await f.Compare(f.Factory.CreateForService("a"), 2, 2, "a");
        await f.Compare(f.Factory.CreateForService("b"), 2, 2, "b");
        Assert.Equal(opens, f.Storage.Opens);
    }

    [Fact]
    public async Task Factory_shares_holder_for_normalized_service_id()
    {
        using var f = new Fixture();
        await f.Seed("a", Enumerable.Range(1, 10).Select(n => Event(n)).ToArray());
        await f.Compare(f.Factory.CreateForService("A"), null, 2);
        var opens = f.Storage.Opens;
        await f.Compare(f.Factory.CreateForService("a"), 2, 2);
        Assert.Equal(opens, f.Storage.Opens);
    }

    [Theory]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public async Task Factory_preserves_lifetime_and_scope_dependencies_but_shares_holder(ServiceLifetime lifetime)
    {
        using var f = new Fixture();
        await f.Seed("a", Enumerable.Range(1, 10).Select(n => Event(n)).ToArray());
        var services = f.Services();
        services.AddScoped<ScopedDependency>();
        ((ICollection<ServiceDescriptor>)services).Add(
            new ServiceDescriptor(typeof(IEventStoreFactory), typeof(ScopedFactory), lifetime));
        services.AddSekibanDcbColdEventHybridRead();
        Assert.Equal(lifetime, services.Last(d => d.ServiceType == typeof(IEventStoreFactory)).Lifetime);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        ScopedDependency firstDependency;
        IEventStoreFactory firstFactory;
        using (var scope = provider.CreateScope())
        {
            firstDependency = scope.ServiceProvider.GetRequiredService<ScopedDependency>();
            firstFactory = scope.ServiceProvider.GetRequiredService<IEventStoreFactory>();
            var anotherFactory = scope.ServiceProvider.GetRequiredService<IEventStoreFactory>();
            if (lifetime == ServiceLifetime.Scoped) Assert.Same(firstFactory, anotherFactory);
            else Assert.NotSame(firstFactory, anotherFactory);
            await f.Compare(firstFactory.CreateForService("a"), null, 2);
            Assert.Equal(1, firstDependency.Calls);
        }
        var opens = f.Storage.Opens;
        using (var scope = provider.CreateScope())
        {
            var dependency = scope.ServiceProvider.GetRequiredService<ScopedDependency>();
            Assert.NotSame(firstDependency, dependency);
            var factory = scope.ServiceProvider.GetRequiredService<IEventStoreFactory>();
            Assert.NotSame(firstFactory, factory);
            await f.Compare(factory.CreateForService("a"), 2, 2);
            Assert.Equal(1, dependency.Calls);
            Assert.Equal(1, firstDependency.Calls);
        }
        Assert.Equal(opens, f.Storage.Opens);
    }

    [Fact]
    public async Task Mutable_singleton_service_provider_is_isolated_sequentially_and_concurrently()
    {
        using var f = new Fixture();
        foreach (var s in new[] { "a", "b" }) await f.Seed(s, Enumerable.Range(1, 10).Select(n => Event(n, s)).ToArray());
        await Read(f.Singleton, null, 2);
        f.Service.Value = "b";
        await f.Compare(f.Singleton, 2, 2, "b");
        async Task Reader(string service)
        {
            f.Service.Value = service;
            for (var i = 0; i < 5; i++) await f.Compare(f.Singleton, 2, 2, service);
        }
        await Task.WhenAll(Task.Run(() => Reader("a")), Task.Run(() => Reader("b")));
    }

    [Fact]
    public async Task Concurrent_misses_produce_equivalent_batches()
    {
        using var f = new Fixture();
        await f.Seed("a", Enumerable.Range(1, 30).Select(n => Event(n)).ToArray());
        f.Storage.BeforeOpen = async () => await Task.Delay(20);
        await Task.WhenAll(Enumerable.Range(0, 6).Select(i => f.Compare(f.Singleton, i, 3)));
        await f.Compare(f.Singleton, 10, 4);
    }

    [Fact]
    public async Task Normal_path_rechecks_whole_entry_after_append_and_replacement()
    {
        using var f = new Fixture();
        var original = Enumerable.Range(1, 10).Select(n => Event(n)).ToArray();
        await f.Seed("a", original);
        await Read(f.Singleton, null, 3);
        await f.Seed("a", original, Enumerable.Range(11, 3).Select(n => Event(n)).ToArray());
        await f.Compare(f.Singleton, 8, 10); // Insufficient retained suffix: reload manifest, reuse unchanged entry.
        await Read(f.Singleton, null, 3);
        var replacement = Enumerable.Range(1, 14).Select(n => Event(n, "replacement")).ToArray();
        await f.Seed("a", replacement); // Same path, changed whole entry.
        var opens = f.Storage.Opens;
        await f.Compare(f.Singleton, 8, 20);
        Assert.Equal(opens + 1, f.Storage.Opens);
    }

    [Theory]
    [InlineData("sha")]
    [InlineData("size")]
    [InlineData("count")]
    [InlineData("created")]
    [InlineData("from")]
    public async Task Normal_path_requires_equality_of_every_manifest_entry_field(string changedField)
    {
        using var f = new Fixture();
        await f.Seed("a", Enumerable.Range(1, 10).Select(n => Event(n)).ToArray());
        await Read(f.Singleton, null, 2);
        var entry = f.Entries["a"][0];
        var changed = changedField switch
        {
            "sha" => entry with { Sha256 = "changed" },
            "size" => entry with { SizeBytes = entry.SizeBytes + 1 },
            "count" => entry with { EventCount = entry.EventCount + 1 },
            "created" => entry with { CreatedAtUtc = entry.CreatedAtUtc.AddSeconds(1) },
            _ => entry with { FromSortableUniqueId = Id(0) }
        };
        await f.WriteManifest("a", [changed]);
        var opens = f.Storage.Opens;
        await f.Compare(f.Singleton, 8, 20);
        Assert.Equal(opens + 1, f.Storage.Opens);
    }

    [Fact]
    public async Task Replaced_tail_with_merged_path_is_released_after_normal_read()
    {
        using var f = new Fixture();
        await f.Seed("a", Enumerable.Range(1, 10).Select(n => Event(n)).ToArray());
        await Read(f.Singleton, null, 2);
        var replacement = Enumerable.Range(1, 15).Select(n => Event(n, "merged")).ToArray();
        await f.Storage.PutAsync("segments/a/merged", JsonlSegmentWriter.Write(replacement), null, default);
        var entry = f.Entries["a"][0] with { Path = "segments/a/merged", ToSortableUniqueId = Id(15), EventCount = 15 };
        await f.WriteManifest("a", [entry]);
        await f.Compare(f.Singleton, 8, 20);
        var opens = f.Storage.Opens;
        await f.Compare(f.Singleton, 2, 2);
        Assert.Equal(opens + 1, f.Storage.Opens);
    }

    [Fact]
    public async Task Every_cursor_and_batch_size_equals_uncached_read()
    {
        using var f = new Fixture(hot: Enumerable.Range(21, 3).Select(n => Event(n)).ToArray());
        await f.Seed("a", Enumerable.Range(1, 10).Select(n => Event(n)).ToArray(), Enumerable.Range(11, 10).Select(n => Event(n)).ToArray());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Read(f.Uncached("a"), null, -1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Read(f.Singleton, null, -1));
        foreach (var count in new int?[] { 0, 1, 2, 5, 10, 25, null })
            foreach (var since in new int?[] { null, 0, 1, 3, 9, 10, 11, 15, 19, 20, 21, 25 })
                await f.Compare(f.Singleton, since, count);
    }

    [Fact]
    public async Task Sequential_reads_including_empty_termination_load_manifest_at_most_twice()
    {
        using var f = new Fixture();
        await f.Seed("a", Enumerable.Range(1, 100).Select(n => Event(n)).ToArray());
        for (var n = 0; n <= 100; n++) await Read(f.Singleton, n == 0 ? null : n, 1);
        Assert.Equal(1, f.Storage.Opens);
        Assert.Equal(2, f.Storage.Manifests);
    }

    [Fact]
    public async Task Fast_read_sets_cold_only_metadata_with_original_manifest_segment_count()
    {
        using var f = new Fixture();
        await f.Seed("a", Enumerable.Range(1, 10).Select(n => Event(n)).ToArray(), Enumerable.Range(11, 10).Select(n => Event(n)).ToArray());
        using (HybridReadProjectionContext.Push("retained-test"))
        {
            await Read(f.Singleton, null, 2);
            var manifests = f.Storage.Manifests;
            await Read(f.Singleton, 2, 2);
            Assert.Equal(manifests, f.Storage.Manifests);
            var metadata = HybridReadProjectionContext.BatchMetadata!;
            Assert.True(metadata.UsedCold);
            Assert.False(metadata.UsedHot);
            Assert.False(metadata.ReachedColdSegmentBoundary);
            Assert.Equal(2, metadata.SegmentCount);
            Assert.Equal(2, metadata.ColdEventsRead);
        }
    }

    [Fact]
    public async Task Aligned_list_reads_with_projection_match_uncached_events_and_boundary_metadata()
    {
        using var f = new Fixture(align: true, hot: Enumerable.Range(21, 3).Select(n => Event(n)).ToArray());
        await f.Seed("a", Enumerable.Range(1, 10).Select(n => Event(n)).ToArray(),
            Enumerable.Range(11, 10).Select(n => Event(n)).ToArray());
        using var context = HybridReadProjectionContext.Push("aligned-retained-test");
        var sawBoundary = false;
        var sawInterior = false;
        foreach (var (since, count) in new (int?, int)[] { (null, 3), (3, 3), (6, 3), (9, 3), (10, 3), (13, 3), (16, 3), (19, 3), (20, 3) })
        {
            var expected = await Read(f.Uncached("a"), since, count);
            var expectedMetadata = HybridReadProjectionContext.BatchMetadata;
            var actual = await Read(f.Singleton, since, count);
            Assert.Equal(expected.Select(e => e.Id), actual.Select(e => e.Id));
            Assert.NotNull(expectedMetadata);
            Assert.Equal(expectedMetadata, HybridReadProjectionContext.BatchMetadata);
            sawBoundary |= expectedMetadata.ReachedColdSegmentBoundary;
            sawInterior |= expectedMetadata.UsedCold && !expectedMetadata.ReachedColdSegmentBoundary;
        }
        Assert.True(sawBoundary);
        Assert.True(sawInterior);
        Assert.Equal(2, f.Storage.Opens);
    }

    [Fact]
    public async Task End_and_idle_release_are_observed_as_new_opens()
    {
        using var f = new Fixture();
        await f.Seed("a", Enumerable.Range(1, 10).Select(n => Event(n)).ToArray());
        await Read(f.Singleton, null, 2);
        await Read(f.Singleton, 2, 20);
        var opens = f.Storage.Opens;
        await Read(f.Singleton, 2, 2);
        Assert.Equal(opens + 1, f.Storage.Opens);
        opens = f.Storage.Opens;
        f.Time.Advance(TimeSpan.FromDays(1));
        await f.Compare(f.Singleton, 4, 2);
        Assert.Equal(opens + 1, f.Storage.Opens);
    }

    [Fact]
    public async Task Stream_neither_fills_nor_uses_nor_releases_list_holder()
    {
        using var f = new Fixture();
        await f.Seed("a", Enumerable.Range(1, 10).Select(n => Event(n)).ToArray());
        var stream = (IStreamingSerializableEventStore)f.Singleton;
        for (var i = 0; i < 3; i++)
            Assert.True((await stream.StreamAllSerializableEventsAsync(Since(i * 2), 2, _ => ValueTask.CompletedTask)).IsSuccess);
        Assert.Equal(3, f.Storage.Opens);
        await Read(f.Singleton, null, 2);
        await stream.StreamAllSerializableEventsAsync(null, null, _ => ValueTask.CompletedTask);
        var opens = f.Storage.Opens;
        await f.Compare(f.Singleton, 2, 2);
        Assert.Equal(opens, f.Storage.Opens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Factory_failure_discards_fill_and_falls_back_to_complete_original_hot_read(bool brokenLine)
    {
        var hot = Enumerable.Range(1, 10).Select(n => Event(n, "hot")).ToArray();
        using var f = new Fixture(hot: hot);
        var cold = Enumerable.Range(1, 10).Select(n => Event(n)).ToArray();
        await f.Seed("a", cold);
        var path = f.Entries["a"][0].Path;
        if (brokenLine)
            await f.Storage.PutAsync(path, JsonlSegmentWriter.Write(cold.Take(5).ToArray()).Concat("broken\n"u8.ToArray()).ToArray(), null, default);
        else await f.Storage.DeleteAsync(path, default);
        var store = f.Factory.CreateForService("a");
        Assert.Equal(hot.Take(2).Select(e => e.Id), (await Read(store, null, 2)).Select(e => e.Id));
        var opens = f.Storage.Opens;
        Assert.Equal(hot.Skip(2).Take(2).Select(e => e.Id), (await Read(store, 2, 2)).Select(e => e.Id));
        Assert.Equal(opens + 1, f.Storage.Opens);
    }

    [Fact]
    public void Factory_capabilities_forward_without_upgrade_and_last_registration_is_decorated()
    {
        using var f = new Fixture();
        Assert.Equal(SekibanDcbCapabilityResolver.DescribeStorage(f.InnerFactory, "hot event store"),
            SekibanDcbCapabilityResolver.DescribeStorage(f.Factory, "hot event store"));
        var innerConditions = SekibanDcbCapabilityResolver.DescribeWriteConditions(f.InnerFactory, "hot event store");
        var decoratedConditions = SekibanDcbCapabilityResolver.DescribeWriteConditions(f.Factory, "hot event store");
        Assert.Equal(innerConditions.ProviderName, decoratedConditions.ProviderName);
        Assert.Equal(innerConditions.SupportedKinds, decoratedConditions.SupportedKinds);
        var services = f.Services();
        services.AddSingleton<IEventStoreFactory>(new DescriptorFactory());
        services.AddSekibanDcbColdEventHybridRead();
        using var sp = services.BuildServiceProvider();
        var decorated = sp.GetRequiredService<IEventStoreFactory>();
        Assert.Equal(StorageDurability.Durable, SekibanDcbCapabilityResolver.DescribeStorage(decorated, "factory").Durability);
        Assert.Equal(new[] { WriteConditionKind.SingleEventUniqueKey }, SekibanDcbCapabilityResolver.DescribeWriteConditions(decorated, "factory").SupportedKinds);
    }

    [Fact]
    public void No_factory_and_later_factory_registration_are_accepted()
    {
        using var f = new Fixture();
        var services = f.Services();
        ((ICollection<ServiceDescriptor>)services).Remove(services.Last(d => d.ServiceType == typeof(IEventStoreFactory)));
        services.AddSekibanDcbColdEventHybridRead();
        using (var sp = services.BuildServiceProvider()) Assert.Null(sp.GetService<IEventStoreFactory>());
        services.AddSingleton<IEventStoreFactory>(f.InnerFactory);
        using var later = services.BuildServiceProvider();
        Assert.Same(f.InnerFactory, later.GetRequiredService<IEventStoreFactory>());
    }

    private sealed class DescriptorFactory : IEventStoreFactory, IStorageDurabilityDescriptorProvider, IWriteConditionCapabilityProvider
    {
        public IEventStore CreateForService(string serviceId) => new ListEventStore([]);
        public StorageDurabilityDescriptor DescribeStorage() => new(StorageDurability.Durable, "test factory");
        public WriteConditionCapabilityDescriptor DescribeWriteConditions() => new(new HashSet<WriteConditionKind> { WriteConditionKind.SingleEventUniqueKey }, "test factory");
    }

    private sealed class ScopedDependency
    {
        public int Calls { get; set; }
    }

    private sealed class ScopedFactory(ScopedDependency dependency) : IEventStoreFactory
    {
        public IEventStore CreateForService(string serviceId)
        {
            dependency.Calls++;
            return new ListEventStore([]);
        }
    }

    private sealed class Factory(IReadOnlyList<SerializableEvent> hot) : IEventStoreFactory
    {
        public IEventStore CreateForService(string serviceId) => new ListEventStore(hot);
    }

    private sealed class TestTime : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }
    private sealed class Service : IServiceIdProvider
    {
        private readonly AsyncLocal<string?> _value = new();
        public string Value { set => _value.Value = value; }
        public string GetCurrentServiceId() => _value.Value ?? "a";
    }
    private sealed class Fixture : IDisposable
    {
        public CountingStorage Storage { get; } = new();
        public Service Service { get; } = new();
        public TestTime Time { get; } = new();
        public Dictionary<string, ColdSegmentInfo[]> Entries { get; } = new();
        private readonly IColdSegmentFormatHandler _handler;
        private readonly IReadOnlyList<SerializableEvent> _hot;
        private readonly ServiceProvider _sp;
        private readonly bool _align;
        public IEventStoreFactory InnerFactory { get; }
        public Fixture(IColdSegmentFormatHandler? handler = null, IReadOnlyList<SerializableEvent>? hot = null, bool align = false)
        {
            _align = align;
            _hot = hot ?? [];
            _handler = handler ?? new JsonlColdSegmentFormatHandler();
            InnerFactory = new Factory(_hot);
            var services = Services();
            services.AddSekibanDcbColdEventHybridRead();
            _sp = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });
        }
        public ServiceCollection Services()
        {
            var services = new ServiceCollection();
            services.AddSingleton<Microsoft.Extensions.Logging.ILogger<HybridEventStore>>(Microsoft.Extensions.Logging.Abstractions.NullLogger<HybridEventStore>.Instance);
            services.AddSingleton<IEventStore>(new ListEventStore(_hot));
            services.AddSingleton(InnerFactory);
            services.AddSingleton<IColdObjectStorage>(Storage);
            services.AddSingleton(_handler);
            services.AddSingleton<IServiceIdProvider>(Service);
            services.AddSingleton<TimeProvider>(Time);
            services.AddSingleton<IOptions<ColdEventStoreOptions>>(Options.Create(new ColdEventStoreOptions { Enabled = true, AlignCatchUpReadsToSegmentBoundary = _align }));
            return services;
        }
        public IEventStore Singleton => _sp.GetRequiredService<IEventStore>();
        public IEventStoreFactory Factory => _sp.GetRequiredService<IEventStoreFactory>();
        public IEventStore Uncached(string service) => new HybridEventStore(new ListEventStore(_hot), Storage.Inner, _handler,
            new FixedServiceIdProvider(service), Options.Create(new ColdEventStoreOptions { Enabled = true, AlignCatchUpReadsToSegmentBoundary = _align }), Microsoft.Extensions.Logging.Abstractions.NullLogger<HybridEventStore>.Instance);
        public async Task Compare(IEventStore cached, int? since, int? count, string service = "a")
            => Assert.Equal((await Read(Uncached(service), since, count)).Select(e => e.Id), (await Read(cached, since, count)).Select(e => e.Id));
        public async Task Seed(string service, params SerializableEvent[][] segments)
        {
            var entries = new List<ColdSegmentInfo>();
            for (var i = 0; i < segments.Length; i++)
            {
                var events = segments[i];
                var path = $"segments/{service}/{i}";
                byte[] data;
                if (_handler is JsonlColdSegmentFormatHandler) data = JsonlSegmentWriter.Write(events);
                else
                {
                    await using var builder = await _handler.CreateBuilderAsync(events[0], default);
                    foreach (var evt in events.Skip(1)) await builder.AppendAsync(evt, default);
                    var artifact = await builder.CompleteAsync(service, default);
                    await using var stream = File.OpenRead(artifact.FilePath);
                    using var memory = new MemoryStream();
                    await stream.CopyToAsync(memory);
                    data = memory.ToArray();
                }
                await Storage.PutAsync(path, data, null, default);
                // Deterministic entries remain equal when the same segment appears in a newer manifest.
                entries.Add(new(path, events[0].SortableUniqueIdValue, events[^1].SortableUniqueIdValue, events.Length, data.Length, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)), DateTimeOffset.UnixEpoch));
            }
            Entries[service] = entries.ToArray();
            await WriteManifest(service, entries);
        }
        public async Task WriteManifest(string service, IReadOnlyList<ColdSegmentInfo> entries)
        {
            var manifest = new ColdManifest(service, "v1", entries[^1].ToSortableUniqueId, entries, DateTimeOffset.UtcNow);
            await Storage.PutAsync(ColdStoragePaths.ManifestPath(service), JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), null, default);
        }
        public void Dispose() => _sp.Dispose();
    }
}

internal sealed class CountingStorage : IColdObjectStorage
{
    public InMemoryColdObjectStorage Inner { get; } = new();
    private int _opens, _manifests;
    public int Opens => _opens;
    public int Manifests => _manifests;
    public Func<Task>? BeforeOpen { get; set; }
    public Task<ResultBox<ColdStorageObject>> GetAsync(string path, CancellationToken ct)
    {
        if (path.EndsWith("manifest.json", StringComparison.Ordinal)) Interlocked.Increment(ref _manifests);
        return Inner.GetAsync(path, ct);
    }
    public async Task<ResultBox<Stream>> OpenReadAsync(string path, CancellationToken ct)
    {
        Interlocked.Increment(ref _opens);
        if (BeforeOpen is not null) await BeforeOpen();
        return await Inner.OpenReadAsync(path, ct);
    }
    public Task<ResultBox<bool>> PutAsync(string path, Stream data, string? expectedETag, CancellationToken ct) => Inner.PutAsync(path, data, expectedETag, ct);
    public Task<ResultBox<bool>> PutAsync(string path, byte[] data, string? expectedETag, CancellationToken ct) => Inner.PutAsync(path, data, expectedETag, ct);
    public Task<ResultBox<IReadOnlyList<string>>> ListAsync(string prefix, CancellationToken ct) => Inner.ListAsync(prefix, ct);
    public Task<ResultBox<bool>> DeleteAsync(string path, CancellationToken ct) => Inner.DeleteAsync(path, ct);
}
