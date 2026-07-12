using Microsoft.Azure.Cosmos;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.CosmosDb.Tags;
using Sekiban.Dcb.Tags;
using System.Net;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     Covers the roll-forward tag-write retry: convergence, exhaustion, backoff shape, Retry-After,
///     cancellation, and the rule that a corruption error is never retried.
/// </summary>
public class CosmosTagWriteRetryExecutorTests
{
    /// <summary>
    ///     Virtual clock and jitter: delays are recorded, never waited on, so the tests are deterministic
    ///     and instant.
    /// </summary>
    private sealed class FakeRetryScheduler : ICosmosRetryScheduler
    {
        private readonly double _jitter;

        public FakeRetryScheduler(double jitter = 0) => _jitter = jitter;

        public List<TimeSpan> Delays { get; } = new();
        public DateTimeOffset UtcNow { get; private set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Delays.Add(delay);
            UtcNow = UtcNow.Add(delay);
            return Task.CompletedTask;
        }

        public double NextJitter() => _jitter;
    }

    private static readonly IReadOnlyList<Guid> EventIds = new[] { Guid.NewGuid(), Guid.NewGuid() };

    private static List<TagWriteResult> Results() =>
        new() { new TagWriteResult("Student:1", 1, DateTimeOffset.UtcNow) };

    /// <summary>A 429 that carries a Retry-After hint, as Cosmos sends when it throttles.</summary>
    private sealed class ThrottledCosmosException : CosmosException
    {
        private readonly TimeSpan? _retryAfter;

        public ThrottledCosmosException(TimeSpan? retryAfter)
            : base("throttled", HttpStatusCode.TooManyRequests, 429, "activity", 1.0) =>
            _retryAfter = retryAfter;

        public override TimeSpan? RetryAfter => _retryAfter;
    }

    private static CosmosException Throttled(TimeSpan? retryAfter) => new ThrottledCosmosException(retryAfter);

    private static Task<List<TagWriteResult>> ExecuteAsync(
        Func<Task<List<TagWriteResult>>> writeTags,
        CosmosTagWriteRetryOptions options,
        ICosmosRetryScheduler scheduler,
        CancellationToken cancellationToken = default) =>
        CosmosTagWriteRetryExecutor.ExecuteAsync(
            writeTags,
            EventIds,
            options,
            scheduler,
            null,
            cancellationToken);

    [Fact]
    public async Task Should_Return_Immediately_When_The_First_Attempt_Succeeds()
    {
        var scheduler = new FakeRetryScheduler();
        var attempts = 0;

        var results = await ExecuteAsync(
            () =>
            {
                attempts++;
                return Task.FromResult(Results());
            },
            new CosmosTagWriteRetryOptions(),
            scheduler);

        Assert.Single(results);
        Assert.Equal(1, attempts);
        Assert.Empty(scheduler.Delays);
    }

    [Fact]
    public async Task Should_Retry_Until_The_Tag_Write_Converges()
    {
        var scheduler = new FakeRetryScheduler();
        var attempts = 0;

        var results = await ExecuteAsync(
            () =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new InvalidOperationException("transient tag write failure");
                }

                return Task.FromResult(Results());
            },
            new CosmosTagWriteRetryOptions { MaxAttempts = 5, JitterRatio = 0 },
            scheduler);

        Assert.Single(results);
        Assert.Equal(3, attempts);
        Assert.Equal(2, scheduler.Delays.Count);
    }

    [Fact]
    public async Task Should_Back_Off_Exponentially_Up_To_MaxBackoff()
    {
        var scheduler = new FakeRetryScheduler();
        var options = new CosmosTagWriteRetryOptions
        {
            MaxAttempts = 5,
            InitialBackoff = TimeSpan.FromMilliseconds(100),
            MaxBackoff = TimeSpan.FromMilliseconds(300),
            MaxTotalDuration = TimeSpan.FromMinutes(1),
            JitterRatio = 0
        };

        await Assert.ThrowsAsync<CosmosTagWriteExhaustedException>(
            () => ExecuteAsync(
                () => throw new InvalidOperationException("always fails"),
                options,
                scheduler));

        Assert.Equal(
            new[]
            {
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(200),
                TimeSpan.FromMilliseconds(300),
                TimeSpan.FromMilliseconds(300)
            },
            scheduler.Delays);
    }

    [Fact]
    public async Task Jitter_Should_Shorten_The_Backoff_Within_Its_Ratio()
    {
        // A jitter draw of 1.0 with ratio 0.25 takes the full quarter off: 200ms -> 150ms.
        var scheduler = new FakeRetryScheduler(1.0);
        var options = new CosmosTagWriteRetryOptions
        {
            MaxAttempts = 2,
            InitialBackoff = TimeSpan.FromMilliseconds(200),
            JitterRatio = 0.25
        };

        await Assert.ThrowsAsync<CosmosTagWriteExhaustedException>(
            () => ExecuteAsync(() => throw new InvalidOperationException("always fails"), options, scheduler));

        Assert.Equal(TimeSpan.FromMilliseconds(150), Assert.Single(scheduler.Delays));
    }

    [Fact]
    public async Task Should_Honor_The_Cosmos_RetryAfter_Hint_When_Throttled()
    {
        var scheduler = new FakeRetryScheduler();
        var options = new CosmosTagWriteRetryOptions
        {
            MaxAttempts = 2,
            InitialBackoff = TimeSpan.FromMilliseconds(50),
            MaxBackoff = TimeSpan.FromSeconds(10),
            JitterRatio = 0
        };

        await Assert.ThrowsAsync<CosmosTagWriteExhaustedException>(
            () => ExecuteAsync(
                () => throw Throttled(TimeSpan.FromSeconds(3)),
                options,
                scheduler));

        // Cosmos told us how long to wait; that beats our own curve.
        Assert.Equal(TimeSpan.FromSeconds(3), Assert.Single(scheduler.Delays));
    }

    [Fact]
    public async Task Should_Exhaust_After_MaxAttempts_And_Name_The_Affected_Events()
    {
        var scheduler = new FakeRetryScheduler();
        var attempts = 0;

        var exception = await Assert.ThrowsAsync<CosmosTagWriteExhaustedException>(
            () => ExecuteAsync(
                () =>
                {
                    attempts++;
                    throw new InvalidOperationException("always fails");
                },
                new CosmosTagWriteRetryOptions { MaxAttempts = 3, JitterRatio = 0 },
                scheduler));

        Assert.Equal(3, attempts);
        Assert.Equal(3, exception.Attempts);
        Assert.Equal(EventIds, exception.EventIds);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public async Task Should_Stop_Once_The_Deadline_Would_Be_Passed()
    {
        var scheduler = new FakeRetryScheduler();
        var options = new CosmosTagWriteRetryOptions
        {
            MaxAttempts = 100,
            InitialBackoff = TimeSpan.FromMilliseconds(400),
            MaxBackoff = TimeSpan.FromSeconds(10),
            MaxTotalDuration = TimeSpan.FromSeconds(1),
            JitterRatio = 0
        };

        var exception = await Assert.ThrowsAsync<CosmosTagWriteExhaustedException>(
            () => ExecuteAsync(() => throw new InvalidOperationException("always fails"), options, scheduler));

        // 400ms, then 800ms would cross the 1s deadline, so it stops rather than starting another attempt.
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(400) }, scheduler.Delays);
        Assert.Equal(2, exception.Attempts);
    }

    [Fact]
    public async Task Should_Not_Retry_A_Corruption_Error()
    {
        var scheduler = new FakeRetryScheduler();
        var attempts = 0;

        await Assert.ThrowsAsync<CosmosTagIndexCorruptionException>(
            () => ExecuteAsync(
                () =>
                {
                    attempts++;
                    throw new CosmosTagIndexCorruptionException(
                        "svc",
                        "Student:1",
                        "svc|Student:1",
                        Guid.NewGuid().ToString(),
                        "expected",
                        "actual");
                },
                new CosmosTagWriteRetryOptions { MaxAttempts = 5 },
                scheduler));

        // Every attempt derives the same content, so a retry would fail identically forever.
        Assert.Equal(1, attempts);
        Assert.Empty(scheduler.Delays);
    }

    [Fact]
    public async Task Should_Observe_Cancellation_Between_Attempts()
    {
        var scheduler = new FakeRetryScheduler();
        using var cts = new CancellationTokenSource();
        var attempts = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ExecuteAsync(
                () =>
                {
                    attempts++;
                    cts.Cancel();
                    throw new InvalidOperationException("transient tag write failure");
                },
                new CosmosTagWriteRetryOptions { MaxAttempts = 5, JitterRatio = 0 },
                scheduler,
                cts.Token));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task MaxAttempts_Of_One_Should_Disable_Retrying()
    {
        var scheduler = new FakeRetryScheduler();
        var attempts = 0;

        await Assert.ThrowsAsync<CosmosTagWriteExhaustedException>(
            () => ExecuteAsync(
                () =>
                {
                    attempts++;
                    throw new InvalidOperationException("always fails");
                },
                new CosmosTagWriteRetryOptions { MaxAttempts = 1 },
                scheduler));

        Assert.Equal(1, attempts);
        Assert.Empty(scheduler.Delays);
    }
}
