using System.Security.Cryptography;

namespace DcbOrleans.WithoutResult.ApiService.ColdEvents;

internal static class ColdStoragePath
{
    public static string Normalize(string path)
        => path.Replace('\\', '/').TrimStart('/');

    public static string ToAbsolute(string basePath, string relative)
    {
        var normalized = Normalize(relative);
        var fullPath = Path.GetFullPath(Path.Combine(basePath, normalized));
        var normalizedBase = Path.GetFullPath(basePath);
        if (!fullPath.StartsWith(normalizedBase + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !string.Equals(fullPath, normalizedBase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Path traversal detected: '{relative}' escapes base directory");
        }
        return fullPath;
    }

    public static string ComputeEtag(byte[] data)
        => Convert.ToHexStringLower(SHA256.HashData(data));

    public static string ComputeLeaseToken()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
}
