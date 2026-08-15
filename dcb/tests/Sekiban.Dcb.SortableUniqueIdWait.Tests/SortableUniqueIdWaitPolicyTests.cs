using System.Text.Json;
using System.Reflection;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Queries;

namespace Sekiban.Dcb.SortableUniqueIdWait.Tests;

public sealed class SortableUniqueIdWaitPolicyTests
{
    [Fact]
    public void Architecture_AllWithResultEntrypointsBindTheSharedPolicy()
    {
        WaitArchitectureAssertions.AssertAll(
            new WaitArchitectureRoute(
                "orleans-with-result-single",
                typeof(Sekiban.Dcb.Orleans.OrleansDcbExecutor),
                typeof(IQueryCommon<>),
                "WaitForSortableUniqueIdIfNeeded",
                SortableUniqueIdWaitSurface.OrleansWithResultSingle),
            new WaitArchitectureRoute(
                "orleans-with-result-list",
                typeof(Sekiban.Dcb.Orleans.OrleansDcbExecutor),
                typeof(IListQueryCommon<>),
                "WaitForSortableUniqueIdIfNeeded",
                SortableUniqueIdWaitSurface.OrleansWithResultList),
            new WaitArchitectureRoute(
                "in-memory-strict-single",
                typeof(CoreGeneralSekibanExecutor),
                typeof(IQueryCommon<>),
                "WaitForStrictSortableUniqueIdIfNeededAsync",
                SortableUniqueIdWaitSurface.InMemorySingle),
            new WaitArchitectureRoute(
                "in-memory-strict-list",
                typeof(CoreGeneralSekibanExecutor),
                typeof(IListQueryCommon<>),
                "WaitForStrictSortableUniqueIdIfNeededAsync",
                SortableUniqueIdWaitSurface.InMemoryList));
    }

    [Fact]
    public async Task StrictTimeout_UsesAdaptiveClockAndCarriesCurrentPosition()
    {
        var clock = new FakeClock(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var target = SortableUniqueId.Generate(clock.UtcNow.UtcDateTime.AddSeconds(-6), Guid.Empty);
        var policy = CreatePolicy(clock);
        var probes = 0;

        var result = await policy.WaitAsync(
            target,
            SortableUniqueIdWaitSurface.InMemorySingle,
            SortableUniqueIdWaitMode.Strict,
            _ => Task.FromResult(++probes > int.MaxValue),
            _ => Task.FromResult<string?>("observed-position"));

        Assert.True(result.TimedOut);
        Assert.Equal(TimeSpan.FromSeconds(5), result.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(5), result.Elapsed);
        Assert.Equal("observed-position", result.LastObservedSortableUniqueId);
        Assert.Equal(25, probes);
    }

    [Fact]
    public async Task ArrivalAfterOnePoll_IsSuccessfulWithoutRealSleep()
    {
        var clock = new FakeClock(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var target = SortableUniqueId.Generate(clock.UtcNow.UtcDateTime, Guid.Empty);
        var policy = CreatePolicy(clock);
        var probes = 0;

        var result = await policy.WaitAsync(
            target,
            SortableUniqueIdWaitSurface.OrleansWithResultSingle,
            SortableUniqueIdWaitMode.Strict,
            _ => Task.FromResult(++probes == 2),
            _ => Task.FromResult<string?>(null));

        Assert.False(result.TimedOut);
        Assert.Equal(TimeSpan.FromMilliseconds(200), result.Elapsed);
        Assert.Equal(2, probes);
    }

    [Fact]
    public async Task DiagnosticFailureDoesNotReplaceTimeout_AndCancellationOrProbeFaultIsPreserved()
    {
        var clock = new FakeClock(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var oldTarget = SortableUniqueId.Generate(clock.UtcNow.UtcDateTime.AddSeconds(-6), Guid.Empty);
        var policy = CreatePolicy(clock);

        var timeout = await policy.WaitAsync(
            oldTarget,
            SortableUniqueIdWaitSurface.InMemoryList,
            SortableUniqueIdWaitMode.Strict,
            _ => Task.FromResult(false),
            _ => Task.FromException<string?>(new InvalidOperationException("diagnostic failed")));

        Assert.True(timeout.TimedOut);
        Assert.Null(timeout.LastObservedSortableUniqueId);

        var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => policy.WaitAsync(
            oldTarget,
            SortableUniqueIdWaitSurface.OrleansWithoutResultSingle,
            SortableUniqueIdWaitMode.Strict,
            _ => Task.FromResult(false),
            _ => Task.FromResult<string?>(null),
            cancellation.Token));

        var fault = new InvalidOperationException("probe failed");
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => policy.WaitAsync(
            oldTarget,
            SortableUniqueIdWaitSurface.OrleansWithoutResultList,
            SortableUniqueIdWaitMode.Legacy,
            _ => Task.FromException<bool>(fault)));
        Assert.Same(fault, thrown);
    }

    [Fact]
    public void StrictMarkerAddsNoSerializedMembers_AndExceptionIsTyped()
    {
        Assert.Empty(
            typeof(IStrictWaitForSortableUniqueId).GetProperties(
                System.Reflection.BindingFlags.DeclaredOnly |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance));
        Assert.Contains(
            typeof(IStrictWaitForSortableUniqueId).GetInterfaces(),
            interfaceType => interfaceType == typeof(IWaitForSortableUniqueId));

        var exception = new SortableUniqueIdWaitTimeoutException(
            "target",
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5),
            "observed");

        Assert.IsAssignableFrom<TimeoutException>(exception);
        Assert.Equal("target", exception.TargetSortableUniqueId);
        Assert.Equal(TimeSpan.FromSeconds(5), exception.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(5), exception.Elapsed);
        Assert.Equal("observed", exception.LastObservedSortableUniqueId);
    }

    [Fact]
    public void AdaptiveTimeout_PreservesLegacyFiveSecondBoundary()
    {
        var clock = new FakeClock(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var old = SortableUniqueId.Generate(clock.UtcNow.UtcDateTime.AddSeconds(-6), Guid.Empty);
        var exactBoundary = SortableUniqueId.Generate(clock.UtcNow.UtcDateTime.AddSeconds(-5), Guid.Empty);
        var future = SortableUniqueId.Generate(clock.UtcNow.UtcDateTime.AddSeconds(1), Guid.Empty);

        Assert.Equal(5_000, SortableUniqueIdWaitHelper.CalculateAdaptiveTimeout(old, clock));
        Assert.Equal(30_000, SortableUniqueIdWaitHelper.CalculateAdaptiveTimeout(exactBoundary, clock));
        Assert.Equal(30_000, SortableUniqueIdWaitHelper.CalculateAdaptiveTimeout(future, clock));
        Assert.Equal(30_000, SortableUniqueIdWaitHelper.CalculateAdaptiveTimeout("not-a-sortable-id", clock));
    }

    [Fact]
    public async Task ThirtySecondTimeoutCases_UseFakeClockWithoutRealSleep()
    {
        var baseNow = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(baseNow);
        var targets = new[]
        {
            SortableUniqueId.Generate(clock.UtcNow.UtcDateTime.AddSeconds(-5), Guid.Empty),
            SortableUniqueId.Generate(clock.UtcNow.UtcDateTime.AddSeconds(1), Guid.Empty),
            "not-a-sortable-id"
        };

        foreach (var target in targets)
        {
            clock = new FakeClock(baseNow);
            var policy = CreatePolicy(clock);
            var probes = 0;
            var result = await policy.WaitAsync(
                target,
                SortableUniqueIdWaitSurface.InMemorySingle,
                SortableUniqueIdWaitMode.Strict,
                _ => Task.FromResult(++probes > int.MaxValue),
                _ => Task.FromResult<string?>(null));

            Assert.True(result.TimedOut);
            Assert.Equal(TimeSpan.FromSeconds(30), result.Timeout);
            Assert.Equal(TimeSpan.FromSeconds(30), result.Elapsed);
            Assert.Equal(150, probes);
        }
    }

    [Fact]
    public void G17StrictMarkerDoesNotChangeTheNegativeWireShape()
    {
        var wire = JsonSerializer.Serialize(new StrictWireQuery("target", "payload"));
        using var document = JsonDocument.Parse(wire);
        var properties = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();

        Assert.Equal(["WaitForSortableUniqueId", "Value"], properties);
        Assert.DoesNotContain(properties, property => property.Contains("strict", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, property => property.Contains("fail", StringComparison.OrdinalIgnoreCase));
    }

    private static SortableUniqueIdWaitPolicy CreatePolicy(FakeClock clock) =>
        new(clock, (delay, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            clock.Advance(delay);
            return Task.CompletedTask;
        });

    private sealed record StrictWireQuery(string? WaitForSortableUniqueId, string Value) : IStrictWaitForSortableUniqueId;

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _utcNow;
        private long _timestamp;

        public FakeClock(DateTimeOffset utcNow) => _utcNow = utcNow;

        public DateTimeOffset UtcNow => _utcNow;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan amount)
        {
            _utcNow += amount;
            _timestamp += amount.Ticks;
        }
    }
}
