namespace Sekiban.Dcb.ColdEvents;

public interface IColdLeaseManager
{
    Task<ColdLease> AcquireAsync(string leaseId, TimeSpan duration, CancellationToken ct);

    Task<ColdLease> RenewAsync(ColdLease lease, TimeSpan duration, CancellationToken ct);

    Task<bool> ReleaseAsync(ColdLease lease, CancellationToken ct);
}
