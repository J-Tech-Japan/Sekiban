using System.Security.Cryptography;
using System.Text.Json;

namespace Sekiban.Dcb.ColdEvents;

public sealed class StorageBackedColdLeaseManager : IColdLeaseManager
{
    private readonly IColdObjectStorage _storage;

    public StorageBackedColdLeaseManager(IColdObjectStorage storage)
    {
        _storage = storage;
    }

    public Task<ColdLease> AcquireAsync(string leaseId, TimeSpan duration, CancellationToken ct)
        => UpsertLeaseAsync(leaseId, duration, expectedToken: null, requireOwnership: false, ct);

    public Task<ColdLease> RenewAsync(ColdLease lease, TimeSpan duration, CancellationToken ct)
        => UpsertLeaseAsync(lease.LeaseId, duration, lease.Token, requireOwnership: true, ct);

    public async Task<bool> ReleaseAsync(ColdLease lease, CancellationToken ct)
    {
        var path = LeasePath(lease.LeaseId);
        var existing = await _storage.GetAsync(path, ct)
            ?? throw new InvalidOperationException($"Lease {lease.LeaseId} was not found");

        var storedLease = DeserializeLease(existing.Data, existing.ETag, lease.LeaseId);
        if (storedLease.Token != lease.Token)
        {
            throw new InvalidOperationException($"Lease {lease.LeaseId} is not held by token");
        }

        return await _storage.DeleteAsync(path, ct);
    }

    private async Task<ColdLease> UpsertLeaseAsync(
        string leaseId,
        TimeSpan duration,
        string? expectedToken,
        bool requireOwnership,
        CancellationToken ct)
    {
        var path = LeasePath(leaseId);
        var now = DateTimeOffset.UtcNow;
        var existingLease = await LoadExistingLeaseAsync(path, leaseId, ct);
        ValidateExistingLease(existingLease.Lease, leaseId, expectedToken, requireOwnership, now);

        var token = expectedToken ?? ComputeLeaseToken();
        var lease = new ColdLease(leaseId, token, now.Add(duration));
        var payload = JsonSerializer.SerializeToUtf8Bytes(lease);

        await _storage.PutAsync(path, payload, existingLease.ETag, ct);

        if (existingLease.ETag is null && !requireOwnership)
        {
            await VerifyAcquiredLeaseAsync(path, leaseId, lease.Token, ct);
        }

        return lease;
    }

    private async Task<StoredLeaseState> LoadExistingLeaseAsync(
        string path,
        string leaseId,
        CancellationToken ct)
    {
        var existing = await _storage.GetAsync(path, ct);
        if (existing is null)
        {
            return new StoredLeaseState(null, null);
        }

        return new StoredLeaseState(DeserializeLease(existing.Data, existing.ETag, leaseId), existing.ETag);
    }

    private static void ValidateExistingLease(
        ColdLease? storedLease,
        string leaseId,
        string? expectedToken,
        bool requireOwnership,
        DateTimeOffset now)
    {
        if (storedLease is null)
        {
            if (requireOwnership)
            {
                throw new InvalidOperationException($"Lease {leaseId} is not held by token");
            }

            return;
        }

        if (requireOwnership && !string.Equals(storedLease.Token, expectedToken, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Lease {leaseId} is not held by token");
        }

        if (!requireOwnership && storedLease.ExpiresAt > now)
        {
            throw new InvalidOperationException($"Lease {leaseId} is already held");
        }
    }

    private async Task VerifyAcquiredLeaseAsync(
        string path,
        string leaseId,
        string expectedToken,
        CancellationToken ct)
    {
        var verify = await _storage.GetAsync(path, ct) ?? await ThrowMissingObjectAsync(path, ct);

        var verifiedLease = DeserializeLease(verify.Data, verify.ETag, leaseId);
        if (verifiedLease.Token != expectedToken)
        {
            throw new InvalidOperationException($"Lease {leaseId} was acquired by another writer");
        }
    }

    /// <summary>
    ///     Surfaces the storage's own missing-object failure (as 10.x did when it forwarded the failed read), by asking
    ///     the storage to open the object it just reported as absent.
    /// </summary>
    private async Task<ColdStorageObject> ThrowMissingObjectAsync(string path, CancellationToken ct)
    {
        await using (await _storage.OpenReadAsync(path, ct))
        {
        }

        throw new KeyNotFoundException($"Cold object not found: {path}");
    }

    private static string LeasePath(string leaseId)
        => $"control/{leaseId}/lease.json";

    private static ColdLease DeserializeLease(byte[] data, string etag, string leaseId)
        => JsonSerializer.Deserialize<ColdLease>(data)
            ?? throw new InvalidOperationException($"Failed to parse lease {leaseId} (etag:{etag})");

    private static string ComputeLeaseToken()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    private sealed record StoredLeaseState(ColdLease? Lease, string? ETag);
}
