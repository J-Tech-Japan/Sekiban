using System.Security.Cryptography;
using System.Text;

namespace Sekiban.Dcb.Snapshots;

public static class SnapshotLocalCachePath
{
    public static string Build(
        string cacheRoot,
        string providerName,
        string storageNamespace,
        string key)
    {
        if (string.IsNullOrWhiteSpace(cacheRoot))
        {
            throw new ArgumentException("Cache root is required.", nameof(cacheRoot));
        }

        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new ArgumentException("Provider name is required.", nameof(providerName));
        }

        if (string.IsNullOrWhiteSpace(storageNamespace))
        {
            throw new ArgumentException("Storage namespace is required.", nameof(storageNamespace));
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key is required.", nameof(key));
        }

        var namespaceHash = ComputeHash($"{providerName}\n{storageNamespace}");
        var keyHash = ComputeHash(key);
        return Path.Combine(cacheRoot, namespaceHash, keyHash + ".bin");
    }

    private static string ComputeHash(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
