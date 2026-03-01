using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb.ColdEvents;

namespace DcbOrleans.WithoutResult.ApiService.ColdEvents;

public sealed class StorageBackedColdLeaseManager : IColdLeaseManager
{
    private readonly IColdObjectStorage _storage;

    public StorageBackedColdLeaseManager(IColdObjectStorage storage)
    {
        _storage = storage;
    }

    public Task<ResultBox<ColdLease>> AcquireAsync(string leaseId, TimeSpan duration, CancellationToken ct)
        => UpsertLeaseAsync(leaseId, duration, expectedToken: null, requireOwnership: false, ct);

    public Task<ResultBox<ColdLease>> RenewAsync(ColdLease lease, TimeSpan duration, CancellationToken ct)
        => UpsertLeaseAsync(lease.LeaseId, duration, lease.Token, requireOwnership: true, ct);

    public async Task<ResultBox<bool>> ReleaseAsync(ColdLease lease, CancellationToken ct)
    {
        var path = LeasePath(lease.LeaseId);
        var existing = await _storage.GetAsync(path, ct);
        if (!existing.IsSuccess)
        {
            if (!IsNotFound(existing.GetException()))
            {
                return ResultBox.Error<bool>(existing.GetException());
            }
            return ResultBox.Error<bool>(new InvalidOperationException($"Lease {lease.LeaseId} was not found"));
        }

        var (storedLease, _) = DeserializeLease(existing.GetValue().Data, existing.GetValue().ETag, lease.LeaseId);
        if (storedLease is null || storedLease.Token != lease.Token)
        {
            return ResultBox.Error<bool>(new InvalidOperationException($"Lease {lease.LeaseId} is not held by token"));
        }

        return await _storage.DeleteAsync(path, ct);
    }

    private async Task<ResultBox<ColdLease>> UpsertLeaseAsync(
        string leaseId,
        TimeSpan duration,
        string? expectedToken,
        bool requireOwnership,
        CancellationToken ct)
    {
        var path = LeasePath(leaseId);
        var now = DateTimeOffset.UtcNow;

        var existing = await _storage.GetAsync(path, ct);
        string? expectedEtag = null;
        if (existing.IsSuccess)
        {
            var objectValue = existing.GetValue();
            expectedEtag = objectValue.ETag;
            var (storedLease, error) = DeserializeLease(objectValue.Data, objectValue.ETag, leaseId);
            if (error is not null)
            {
                return ResultBox.Error<ColdLease>(error);
            }

            if (storedLease is not null)
            {
                if (requireOwnership && !string.Equals(storedLease.Token, expectedToken, StringComparison.Ordinal))
                {
                    return ResultBox.Error<ColdLease>(new InvalidOperationException($"Lease {leaseId} is not held by token"));
                }

                if (!requireOwnership && storedLease.ExpiresAt > now)
                {
                    return ResultBox.Error<ColdLease>(new InvalidOperationException($"Lease {leaseId} is already held"));
                }
            }
        }
        else if (!IsNotFound(existing.GetException()))
        {
            return ResultBox.Error<ColdLease>(existing.GetException());
        }
        else if (requireOwnership)
        {
            return ResultBox.Error<ColdLease>(new InvalidOperationException($"Lease {leaseId} is not held by token"));
        }

        var token = expectedToken ?? ColdStoragePath.ComputeLeaseToken();
        var lease = new ColdLease(leaseId, token, now.Add(duration));
        var payload = JsonSerializer.SerializeToUtf8Bytes(lease);

        var putResult = await _storage.PutAsync(path, payload, expectedEtag, ct);
        if (!putResult.IsSuccess)
        {
            return ResultBox.Error<ColdLease>(putResult.GetException());
        }

        // For first-write acquisition (expectedEtag == null), verify persisted owner token.
        if (expectedEtag is null && !requireOwnership)
        {
            var verify = await _storage.GetAsync(path, ct);
            if (!verify.IsSuccess)
            {
                return ResultBox.Error<ColdLease>(verify.GetException());
            }

            var (verifiedLease, verifyError) = DeserializeLease(verify.GetValue().Data, verify.GetValue().ETag, leaseId);
            if (verifyError is not null)
            {
                return ResultBox.Error<ColdLease>(verifyError);
            }
            if (verifiedLease is null || verifiedLease.Token != lease.Token)
            {
                return ResultBox.Error<ColdLease>(new InvalidOperationException(
                    $"Lease {leaseId} was acquired by another writer"));
            }
        }

        return ResultBox.FromValue(lease);
    }

    private static string LeasePath(string leaseId)
        => $"control/{leaseId}/lease.json";

    private static (ColdLease? Lease, Exception? Error) DeserializeLease(byte[] data, string etag, string leaseId)
    {
        try
        {
            var lease = JsonSerializer.Deserialize<ColdLease>(data);
            if (lease is null)
            {
                return (null, new InvalidOperationException($"Failed to parse lease {leaseId} (etag:{etag})"));
            }

            return (lease, null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    private static bool IsNotFound(Exception ex)
        => ex is FileNotFoundException or DirectoryNotFoundException or KeyNotFoundException;
}
