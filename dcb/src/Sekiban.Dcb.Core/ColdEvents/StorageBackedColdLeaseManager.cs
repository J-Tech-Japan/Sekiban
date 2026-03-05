using System.Security.Cryptography;
using System.Text.Json;
using ResultBoxes;

namespace Sekiban.Dcb.ColdEvents;

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

        var (storedLease, deserializeError) = DeserializeLease(existing.GetValue().Data, existing.GetValue().ETag, lease.LeaseId);
        if (deserializeError is not null)
        {
            return ResultBox.Error<bool>(deserializeError);
        }

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
        var existingLeaseResult = await LoadExistingLeaseAsync(path, leaseId, ct);
        if (!existingLeaseResult.IsSuccess)
        {
            return ResultBox.Error<ColdLease>(existingLeaseResult.GetException());
        }

        var existingLease = existingLeaseResult.GetValue();
        var validateError = ValidateExistingLease(existingLease.Lease, leaseId, expectedToken, requireOwnership, now);
        if (validateError is not null)
        {
            return ResultBox.Error<ColdLease>(validateError);
        }

        var token = expectedToken ?? ComputeLeaseToken();
        var lease = new ColdLease(leaseId, token, now.Add(duration));
        var payload = JsonSerializer.SerializeToUtf8Bytes(lease);

        var putResult = await _storage.PutAsync(path, payload, existingLease.ETag, ct);
        if (!putResult.IsSuccess)
        {
            return ResultBox.Error<ColdLease>(putResult.GetException());
        }

        if (existingLease.ETag is null && !requireOwnership)
        {
            var verifyResult = await VerifyAcquiredLeaseAsync(path, leaseId, lease.Token, ct);
            if (!verifyResult.IsSuccess)
            {
                return ResultBox.Error<ColdLease>(verifyResult.GetException());
            }
        }

        return ResultBox.FromValue(lease);
    }

    private async Task<ResultBox<StoredLeaseState>> LoadExistingLeaseAsync(
        string path,
        string leaseId,
        CancellationToken ct)
    {
        var existing = await _storage.GetAsync(path, ct);
        if (existing.IsSuccess)
        {
            var objectValue = existing.GetValue();
            var (storedLease, error) = DeserializeLease(objectValue.Data, objectValue.ETag, leaseId);
            if (error is not null)
            {
                return ResultBox.Error<StoredLeaseState>(error);
            }

            return ResultBox.FromValue(new StoredLeaseState(storedLease, objectValue.ETag));
        }

        if (IsNotFound(existing.GetException()))
        {
            return ResultBox.FromValue(new StoredLeaseState(null, null));
        }

        return ResultBox.Error<StoredLeaseState>(existing.GetException());
    }

    private static Exception? ValidateExistingLease(
        ColdLease? storedLease,
        string leaseId,
        string? expectedToken,
        bool requireOwnership,
        DateTimeOffset now)
    {
        if (storedLease is null)
        {
            return requireOwnership
                ? new InvalidOperationException($"Lease {leaseId} is not held by token")
                : null;
        }

        if (requireOwnership && !string.Equals(storedLease.Token, expectedToken, StringComparison.Ordinal))
        {
            return new InvalidOperationException($"Lease {leaseId} is not held by token");
        }

        if (!requireOwnership && storedLease.ExpiresAt > now)
        {
            return new InvalidOperationException($"Lease {leaseId} is already held");
        }

        return null;
    }

    private async Task<ResultBox<bool>> VerifyAcquiredLeaseAsync(
        string path,
        string leaseId,
        string expectedToken,
        CancellationToken ct)
    {
        var verify = await _storage.GetAsync(path, ct);
        if (!verify.IsSuccess)
        {
            return ResultBox.Error<bool>(verify.GetException());
        }

        var (verifiedLease, verifyError) = DeserializeLease(verify.GetValue().Data, verify.GetValue().ETag, leaseId);
        if (verifyError is not null)
        {
            return ResultBox.Error<bool>(verifyError);
        }

        if (verifiedLease is null || verifiedLease.Token != expectedToken)
        {
            return ResultBox.Error<bool>(new InvalidOperationException(
                $"Lease {leaseId} was acquired by another writer"));
        }

        return ResultBox.FromValue(true);
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

    private static string ComputeLeaseToken()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    private sealed record StoredLeaseState(ColdLease? Lease, string? ETag);
}
