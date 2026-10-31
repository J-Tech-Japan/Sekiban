using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

public class TagReservationHelperLoggingTests
{
    private const string TagValue = "test:reservation";
    private static readonly TagWriteReservation Reservation = new("reservation-code", "", TagValue);

    [Theory]
    [InlineData("confirm", true)]
    [InlineData("confirm", false)]
    [InlineData("cancel", true)]
    [InlineData("cancel", false)]
    [InlineData("notify", true)]
    [InlineData("notify", false)]
    public async Task Failure_LogsStructuredWarningThroughHostFactory(string operation, bool resolutionFails)
    {
        using var fixture = new Fixture();
        fixture.Cluster.ResolutionFails = resolutionFails;
        fixture.Grain.Fails = !resolutionFails;

        await RunAsync(operation, fixture.Accessor);

        AssertWarning(fixture, operation, TagValue,
            resolutionFails ? fixture.Cluster.Failure : fixture.Grain.Failure);
        Assert.Equal(typeof(TagReservationHelper).FullName, Assert.Single(fixture.Logger.Categories));
    }

    [Theory]
    [InlineData("confirm")]
    [InlineData("cancel")]
    [InlineData("notify")]
    public async Task Success_DoesNotResolveLoggerOrWarn(string operation)
    {
        using var fixture = new Fixture();
        // Confirm and cancel return false: that outcome must also remain silent.
        await RunAsync(operation, fixture.Accessor);
        Assert.Empty(fixture.Logger.Entries);
        Assert.Equal(0, fixture.Cluster.ServiceProviderCalls);
    }

    [Theory]
    [InlineData("confirm", "throwing-provider")]
    [InlineData("cancel", "throwing-provider")]
    [InlineData("notify", "throwing-provider")]
    [InlineData("confirm", "missing-factory")]
    [InlineData("cancel", "missing-factory")]
    [InlineData("notify", "missing-factory")]
    [InlineData("confirm", "throwing-factory")]
    [InlineData("cancel", "throwing-factory")]
    [InlineData("notify", "throwing-factory")]
    public async Task UnavailableLogger_IsSafeAndCached(string operation, string mode)
    {
        using var fixture = new Fixture(mode);
        fixture.Cluster.ResolutionFails = true;
        await RunAsync(operation, fixture.Accessor);
        await RunAsync(operation, fixture.Accessor);
        Assert.Equal(1, fixture.Cluster.ServiceProviderCalls);
        Assert.Empty(fixture.Logger.Entries);
    }

    [Theory]
    [InlineData("confirm")]
    [InlineData("cancel")]
    [InlineData("notify")]
    public async Task ThrowingLogger_DoesNotEscapeHelper(string operation)
    {
        using var fixture = new Fixture();
        fixture.Grain.Fails = true;
        fixture.Logger.ThrowOnLog = true;
        await RunAsync(operation, fixture.Accessor);
        AssertWarning(fixture, operation, TagValue, fixture.Grain.Failure);
    }

    [Theory]
    [InlineData("confirm")]
    [InlineData("cancel")]
    [InlineData("notify")]
    public async Task ThrowingTag_StillLogsOriginalFailureWithPlaceholder(string operation)
    {
        using var fixture = new Fixture();
        var tag = new TestTag { ThrowOnGetTag = true };
        await RunAsync(operation, fixture.Accessor, tag);
        AssertWarning(fixture, operation, "<unknown>", tag.Failure);
    }

    [Fact]
    public async Task Logger_IsResolvedOnceAcrossFailures()
    {
        using var fixture = new Fixture();
        fixture.Cluster.ResolutionFails = true;
        await RunAsync("confirm", fixture.Accessor);
        await RunAsync("cancel", fixture.Accessor);
        await RunAsync("notify", fixture.Accessor);
        Assert.Equal(1, fixture.Cluster.ServiceProviderCalls);
        Assert.Single(fixture.Logger.Categories);
        Assert.Equal(3, fixture.Logger.Entries.Count);
    }

    private static void AssertWarning(Fixture fixture, string operation, string tag, Exception exception)
    {
        var entry = Assert.Single(fixture.Logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Same(exception, entry.Exception);
        Assert.Equal(operation, entry.Fields["Operation"]);
        Assert.Equal(tag, entry.Fields["Tag"]);
        if (operation != "notify")
            Assert.Equal(Reservation.ReservationCode, entry.Fields["ReservationCode"]);
        else
            Assert.False(entry.Fields.ContainsKey("ReservationCode"));
    }

    private static Task RunAsync(string operation, OrleansActorObjectAccessor accessor, ITag? tag = null)
    {
        tag ??= new TestTag();
        return operation switch
        {
            "confirm" => TagReservationHelper.ConfirmReservationsAsync(accessor, new() { [tag] = Reservation }),
            "cancel" => TagReservationHelper.CancelReservationsAsync(accessor, new() { [tag] = Reservation }),
            "notify" => TagReservationHelper.NotifyNonConsistencyTagsAsync(accessor, [tag], []),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }

    private sealed class Fixture : IDisposable
    {
        public CaptureLoggerFactory Logger { get; } = new();
        public ClusterProxy Cluster { get; }
        public GrainProxy Grain { get; }
        public OrleansActorObjectAccessor Accessor { get; }
        private readonly ServiceProvider _services;

        public Fixture(string mode = "")
        {
            Logger.ThrowOnCreate = mode == "throwing-factory";
            var services = new ServiceCollection();
            if (mode != "missing-factory")
                services.AddSingleton<ILoggerFactory>(Logger);
            _services = services.BuildServiceProvider();
            var grain = DispatchProxy.Create<ITagConsistentGrain, GrainProxy>();
            Grain = (GrainProxy)(object)grain;
            var client = DispatchProxy.Create<IClusterClient, ClusterProxy>();
            Cluster = (ClusterProxy)(object)client;
            Cluster.Services = _services;
            Cluster.Grain = grain;
            Cluster.ThrowOnServiceProvider = mode == "throwing-provider";
            Accessor = new OrleansActorObjectAccessor(client, new InMemoryEventStore(),
                DcbDomainTypesExtensions.Simple(_ => { }));
            Assert.Equal(0, Cluster.ServiceProviderCalls);
        }

        public void Dispose() => _services.Dispose();
    }

    public class ClusterProxy : DispatchProxy
    {
        public IServiceProvider Services { get; set; } = null!;
        public ITagConsistentGrain Grain { get; set; } = null!;
        public bool ResolutionFails { get; set; }
        public bool ThrowOnServiceProvider { get; set; }
        public int ServiceProviderCalls { get; private set; }
        public Exception Failure { get; } = new InvalidOperationException("resolution failed");

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "get_ServiceProvider")
            {
                ServiceProviderCalls++;
                if (ThrowOnServiceProvider) throw new InvalidOperationException("provider failed");
                return Services;
            }
            if (targetMethod?.Name == "GetGrain")
            {
                if (ResolutionFails) throw Failure;
                return Grain;
            }
            throw new InvalidOperationException($"Unexpected client call: {targetMethod?.Name}");
        }
    }

    public class GrainProxy : DispatchProxy
    {
        public bool Fails { get; set; }
        public Exception Failure { get; } = new InvalidOperationException("grain failed");
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name switch
            {
                "ConfirmReservationAsync" or "CancelReservationAsync" =>
                    Fails ? Task.FromException<bool>(Failure) : Task.FromResult(false),
                "NotifyEventWrittenAsync" => Fails ? Task.FromException(Failure) : Task.CompletedTask,
                _ => throw new InvalidOperationException($"Unexpected grain call: {targetMethod?.Name}")
            };
    }

    private sealed class TestTag : ITag
    {
        public bool ThrowOnGetTag { get; init; }
        public Exception Failure { get; } = new InvalidOperationException("tag failed");
        public string GetTag() => ThrowOnGetTag ? throw Failure : TagValue;
        public bool IsConsistencyTag() => false;
        public string GetTagGroup() => "test";
        public string GetTagContent() => "reservation";
    }

    private sealed record Entry(LogLevel Level, Exception? Exception, Dictionary<string, object?> Fields);

    private sealed class CaptureLoggerFactory : ILoggerFactory, ILogger
    {
        public List<Entry> Entries { get; } = [];
        public List<string> Categories { get; } = [];
        public bool ThrowOnLog { get; set; }
        public bool ThrowOnCreate { get; set; }
        public ILogger CreateLogger(string categoryName)
        {
            if (ThrowOnCreate) throw new InvalidOperationException("factory failed");
            Categories.Add(categoryName);
            return this;
        }
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new Entry(logLevel, exception,
                ((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary(pair => pair.Key, pair => pair.Value)));
            if (ThrowOnLog) throw new AggregateException(new InvalidOperationException("logger failed"));
        }
    }
}
