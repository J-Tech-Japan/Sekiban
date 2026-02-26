using System.Threading;

namespace Sekiban.Dcb.Snapshots;

/// <summary>
///     Abstraction for offloading large snapshot payloads to external storage (e.g., Blob Storage).
/// </summary>
public interface IBlobStorageSnapshotAccessor
{
    /// <summary>
    ///     Provider identifier (e.g., "AzureBlobStorage", "InMemory")
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    ///     Writes the snapshot payload from a stream and returns a storage key.
    /// </summary>
    Task<string> WriteAsync(Stream data, string projectorName, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Opens a read stream for the snapshot payload identified by the given key.
    /// </summary>
    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default);
}
