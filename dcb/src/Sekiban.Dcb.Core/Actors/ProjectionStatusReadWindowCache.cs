using System.Collections.Concurrent;
using ResultBoxes;

namespace Sekiban.Dcb.Actors;

/// <summary>
///     Small service-scoped cache for the passive reader's event-store denominator. The key is intentionally only the
///     bound service ID: all projector filters share one head-count sample during a read window.
/// </summary>
public sealed class ProjectionStatusReadWindowCache
{
    private sealed record Sample(long TotalEventCount, DateTimeOffset SampledAtUtc);

    private readonly ConcurrentDictionary<string, Sample> _samples = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sampleGates = new(StringComparer.Ordinal);

    public async Task<(long TotalEventCount, DateTimeOffset SampledAtUtc)> GetOrSampleAsync(
        string serviceId,
        TimeSpan samplingWindow,
        Func<Task<ResultBox<long>>> sampler,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        ArgumentNullException.ThrowIfNull(sampler);
        cancellationToken.ThrowIfCancellationRequested();

        var now = DateTimeOffset.UtcNow;
        if (samplingWindow > TimeSpan.Zero &&
            _samples.TryGetValue(serviceId, out var cached) &&
            now - cached.SampledAtUtc < samplingWindow)
        {
            return (cached.TotalEventCount, cached.SampledAtUtc);
        }

        var gate = _sampleGates.GetOrAdd(serviceId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            now = DateTimeOffset.UtcNow;
            if (samplingWindow > TimeSpan.Zero &&
                _samples.TryGetValue(serviceId, out cached) &&
                now - cached.SampledAtUtc < samplingWindow)
            {
                return (cached.TotalEventCount, cached.SampledAtUtc);
            }

            var sampled = await sampler().ConfigureAwait(false);
            if (!sampled.IsSuccess)
            {
                throw sampled.GetException();
            }

            var sample = new Sample(Math.Max(0, sampled.GetValue()), DateTimeOffset.UtcNow);
            if (samplingWindow > TimeSpan.Zero)
            {
                _samples[serviceId] = sample;
            }

            return (sample.TotalEventCount, sample.SampledAtUtc);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Clears a service sample; useful for deterministic provider tests and host lifecycle boundaries.</summary>
    public void Invalidate(string serviceId)
    {
        if (!string.IsNullOrWhiteSpace(serviceId))
        {
            _samples.TryRemove(serviceId, out _);
        }
    }
}
