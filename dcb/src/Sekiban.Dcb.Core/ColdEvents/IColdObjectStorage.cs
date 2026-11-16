namespace Sekiban.Dcb.ColdEvents;

/// <summary>
///     Object storage used by the cold event store. Failures are reported as exceptions; cancellation is reported as
///     <see cref="OperationCanceledException" />.
/// </summary>
public interface IColdObjectStorage
{
    /// <summary>Returns the object at <paramref name="path" />, or <c>null</c> when no object exists there.</summary>
    Task<ColdStorageObject?> GetAsync(string path, CancellationToken ct);

    /// <summary>
    ///     Opens the object at <paramref name="path" /> for reading. A missing object is a failure: the default
    ///     implementation throws <see cref="FileNotFoundException" />.
    /// </summary>
    async Task<Stream> OpenReadAsync(string path, CancellationToken ct)
    {
        var storageObject = await GetAsync(path, ct).ConfigureAwait(false)
            ?? throw new FileNotFoundException($"Cold object not found: {path}");
        return new MemoryStream(storageObject.Data, writable: false);
    }

    Task<bool> PutAsync(
        string path,
        Stream data,
        string? expectedETag,
        CancellationToken ct);

    Task<bool> PutAsync(
        string path,
        byte[] data,
        string? expectedETag,
        CancellationToken ct);

    Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct);

    /// <summary>Deletes the object; returns <c>false</c> when no object existed.</summary>
    Task<bool> DeleteAsync(string path, CancellationToken ct);
}
