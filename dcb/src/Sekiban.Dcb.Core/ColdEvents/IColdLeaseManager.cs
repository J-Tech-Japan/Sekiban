using ResultBoxes;
namespace Sekiban.Dcb.ColdEvents;

public interface IColdLeaseManager
{
    Task<ResultBox<ColdLease>> AcquireAsync(string leaseId, TimeSpan duration, CancellationToken ct);

    Task<ResultBox<ColdLease>> RenewAsync(ColdLease lease, TimeSpan duration, CancellationToken ct);

    Task<ResultBox<bool>> ReleaseAsync(ColdLease lease, CancellationToken ct);
}
