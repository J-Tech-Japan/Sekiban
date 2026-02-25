using System.Collections.Concurrent;
using ResultBoxes;
using Sekiban.Dcb.ColdEvents;
namespace Sekiban.Dcb.ColdEvents.Tests;

public sealed class InMemoryColdLeaseManager : IColdLeaseManager
{
    private readonly ConcurrentDictionary<string, ColdLease> _leases = new();

    public Task<ResultBox<ColdLease>> AcquireAsync(string leaseId, TimeSpan duration, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        if (_leases.TryGetValue(leaseId, out var existing) && existing.ExpiresAt > now)
        {
            return Task.FromResult(
                ResultBox.Error<ColdLease>(new InvalidOperationException(
                    $"Lease {leaseId} is already held until {existing.ExpiresAt}")));
        }

        var lease = new ColdLease(leaseId, Guid.NewGuid().ToString(), now.Add(duration));
        _leases[leaseId] = lease;
        return Task.FromResult(ResultBox.FromValue(lease));
    }

    public Task<ResultBox<ColdLease>> RenewAsync(ColdLease lease, TimeSpan duration, CancellationToken ct)
    {
        if (!_leases.TryGetValue(lease.LeaseId, out var current) || current.Token != lease.Token)
        {
            return Task.FromResult(
                ResultBox.Error<ColdLease>(new InvalidOperationException(
                    $"Lease {lease.LeaseId} is not held by this token")));
        }

        var renewed = lease with { ExpiresAt = DateTimeOffset.UtcNow.Add(duration) };
        _leases[lease.LeaseId] = renewed;
        return Task.FromResult(ResultBox.FromValue(renewed));
    }

    public Task<ResultBox<bool>> ReleaseAsync(ColdLease lease, CancellationToken ct)
    {
        if (!_leases.TryGetValue(lease.LeaseId, out var current) || current.Token != lease.Token)
        {
            return Task.FromResult(
                ResultBox.Error<bool>(new InvalidOperationException(
                    $"Lease {lease.LeaseId} is not held by this token")));
        }

        _leases.TryRemove(lease.LeaseId, out _);
        return Task.FromResult(ResultBox.FromValue(true));
    }
}
