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
    ///     Writes the snapshot payload bytes and returns a storage key (filename/path) that can be used to retrieve it.
    /// </summary>
    Task<string> WriteAsync(byte[] data, string projectorName, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reads the snapshot payload bytes from storage using the provided key.
    /// </summary>
    Task<byte[]> ReadAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Writes the snapshot payload from a stream and returns a storage key.
    /// </summary>
    Task<string> WriteAsync(Stream data, string projectorName, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Opens a read stream for the snapshot payload identified by the given key.
    /// </summary>
    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default);
}

