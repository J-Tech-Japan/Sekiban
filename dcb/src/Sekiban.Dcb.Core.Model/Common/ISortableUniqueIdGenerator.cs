using Microsoft.Extensions.Logging;

namespace Sekiban.Dcb.Common;

/// <summary>Allocates strictly increasing SortableUniqueIds within one process and accepts persisted tick seeds.</summary>
public interface ISortableUniqueIdGenerator
{
    /// <summary>Allocates a new 30-digit SortableUniqueId using a fresh random suffix.</summary>
    string GenerateNew();

    /// <summary>Atomically advances the logical tick floor without allocating an id.</summary>
    void Seed(long ticks);
}

/// <summary>
///     TimeProvider-backed monotonic SortableUniqueId generator. Logical ticks advance through a CAS loop, so clock
///     rollback and concurrent callers cannot make a later allocation sort before an earlier allocation.
/// </summary>
public sealed class MonotonicSortableUniqueIdGenerator : ISortableUniqueIdGenerator
{
    private static readonly TimeSpan RollbackWarningInterval = TimeSpan.FromMinutes(1);
    private readonly ILogger<MonotonicSortableUniqueIdGenerator> _logger;
    private readonly TimeProvider _timeProvider;
    private long _lastLogicalTicks = DateTime.MinValue.Ticks;
    private long _lastObservedUtcTicks = DateTime.MinValue.Ticks;
    private long _lastRollbackWarningTimestamp = long.MinValue;

    /// <summary>Creates the default generator with <see cref="TimeProvider.System"/>.</summary>
    public MonotonicSortableUniqueIdGenerator() : this(TimeProvider.System)
    {
    }

    /// <summary>Creates a generator using the supplied time source.</summary>
    public MonotonicSortableUniqueIdGenerator(TimeProvider timeProvider)
        : this(timeProvider, Microsoft.Extensions.Logging.Abstractions.NullLogger<MonotonicSortableUniqueIdGenerator>.Instance)
    {
    }

    /// <summary>Creates a generator using the supplied time source and rollback logger.</summary>
    public MonotonicSortableUniqueIdGenerator(
        TimeProvider timeProvider,
        ILogger<MonotonicSortableUniqueIdGenerator> logger)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string GenerateNew()
    {
        var nowTicks = _timeProvider.GetUtcNow().UtcTicks;
        DetectClockRollback(nowTicks);

        while (true)
        {
            var observed = Volatile.Read(ref _lastLogicalTicks);
            if (observed == DateTime.MaxValue.Ticks)
            {
                throw new InvalidOperationException("SortableUniqueId logical ticks are exhausted at DateTime.MaxValue.Ticks.");
            }

            var next = Math.Max(nowTicks, observed + 1);
            if (Interlocked.CompareExchange(ref _lastLogicalTicks, next, observed) == observed)
            {
                return SortableUniqueId.Generate(new DateTime(next, DateTimeKind.Utc), Guid.NewGuid());
            }
        }
    }

    /// <inheritdoc />
    public void Seed(long ticks)
    {
        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
        {
            throw new ArgumentOutOfRangeException(nameof(ticks));
        }

        while (true)
        {
            var observed = Volatile.Read(ref _lastLogicalTicks);
            if (ticks <= observed || Interlocked.CompareExchange(ref _lastLogicalTicks, ticks, observed) == observed)
            {
                return;
            }
        }
    }

    private void DetectClockRollback(long nowTicks)
    {
        while (true)
        {
            var observedPhysical = Volatile.Read(ref _lastObservedUtcTicks);
            if (nowTicks < observedPhysical)
            {
                LogRollbackRateLimited(observedPhysical, nowTicks);
                return;
            }

            if (nowTicks == observedPhysical ||
                Interlocked.CompareExchange(ref _lastObservedUtcTicks, nowTicks, observedPhysical) == observedPhysical)
            {
                return;
            }
        }
    }

    private void LogRollbackRateLimited(long previousTicks, long currentTicks)
    {
        var timestamp = _timeProvider.GetTimestamp();
        while (true)
        {
            var last = Volatile.Read(ref _lastRollbackWarningTimestamp);
            if (last != long.MinValue && _timeProvider.GetElapsedTime(last, timestamp) < RollbackWarningInterval)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _lastRollbackWarningTimestamp, timestamp, last) == last)
            {
                _logger.LogWarning(
                    "System UTC clock moved backwards from {PreviousUtcTicks} to {CurrentUtcTicks}; SortableUniqueId logical ticks remain monotonic.",
                    previousTicks,
                    currentTicks);
                return;
            }
        }
    }
}
