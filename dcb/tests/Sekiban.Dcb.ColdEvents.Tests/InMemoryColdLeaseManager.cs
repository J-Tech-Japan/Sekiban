using System.Collections.Concurrent;
using Sekiban.Dcb.ColdEvents;
namespace Sekiban.Dcb.ColdEvents.Tests;

public sealed class InMemoryColdLeaseManager : IColdLeaseManager
{
    private readonly ConcurrentDictionary<string, ColdLease> _leases = new();

    public Task<ColdLease> AcquireAsync(string leaseId, TimeSpan duration, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        if (_leases.TryGetValue(leaseId, out var existing) && existing.ExpiresAt > now)
        {
            return Task.FromException<ColdLease>(new InvalidOperationException(
                    $"Lease {leaseId} is already held until {existing.ExpiresAt}"));
        }

        var lease = new ColdLease(leaseId, Guid.NewGuid().ToString(), now.Add(duration));
        _leases[leaseId] = lease;
        return Task.FromResult(lease);
    }

    public Task<ColdLease> RenewAsync(ColdLease lease, TimeSpan duration, CancellationToken ct)
    {
        if (!_leases.TryGetValue(lease.LeaseId, out var current) || current.Token != lease.Token)
        {
            return Task.FromException<ColdLease>(new InvalidOperationException(
                    $"Lease {lease.LeaseId} is not held by this token"));
        }

        var renewed = lease with { ExpiresAt = DateTimeOffset.UtcNow.Add(duration) };
        _leases[lease.LeaseId] = renewed;
        return Task.FromResult(renewed);
    }

    public Task<bool> ReleaseAsync(ColdLease lease, CancellationToken ct)
    {
        if (!_leases.TryGetValue(lease.LeaseId, out var current) || current.Token != lease.Token)
        {
            return Task.FromException<bool>(new InvalidOperationException(
                    $"Lease {lease.LeaseId} is not held by this token"));
        }

        _leases.TryRemove(lease.LeaseId, out _);
        return Task.FromResult(true);
    }
}
