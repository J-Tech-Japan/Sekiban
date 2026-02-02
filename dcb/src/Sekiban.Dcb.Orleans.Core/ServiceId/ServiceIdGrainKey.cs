using System;
using Sekiban.Dcb.ServiceId;
namespace Sekiban.Dcb.Orleans.ServiceId;

/// <summary>
///     Helper for encoding/decoding ServiceId into Orleans grain keys and stream namespaces.
/// </summary>
public static class ServiceIdGrainKey
{
    public const char Separator = '|';

    public static string Build(string serviceId, string rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return rawKey;
        }

        var normalized = ServiceIdValidator.NormalizeAndValidate(serviceId);
        if (IsDefaultServiceId(normalized))
        {
            return rawKey;
        }

        if (HasServiceIdPrefix(rawKey))
        {
            return rawKey;
        }

        return $"{normalized}{Separator}{rawKey}";
    }

    public static string BuildStreamNamespace(string baseNamespace, string serviceId)
    {
        if (string.IsNullOrWhiteSpace(baseNamespace))
        {
            return baseNamespace;
        }

        return Build(serviceId, baseNamespace);
    }

    public static (string ServiceId, string RawKey) Parse(string grainKey)
    {
        if (string.IsNullOrEmpty(grainKey))
        {
            return (DefaultServiceIdProvider.DefaultServiceId, grainKey);
        }

        return TryParsePrefix(grainKey, out var serviceId, out var rawKey)
            ? (serviceId, rawKey)
            : (DefaultServiceIdProvider.DefaultServiceId, grainKey);
    }

    public static string Strip(string grainKey) =>
        TryParsePrefix(grainKey, out _, out var rawKey) ? rawKey : grainKey;

    public static bool HasServiceIdPrefix(string grainKey) =>
        TryParsePrefix(grainKey, out _, out _);

    private static bool TryParsePrefix(string grainKey, out string serviceId, out string rawKey)
    {
        serviceId = DefaultServiceIdProvider.DefaultServiceId;
        rawKey = grainKey;

        if (string.IsNullOrEmpty(grainKey))
        {
            return false;
        }

        var separatorIndex = grainKey.IndexOf(Separator);
        if (separatorIndex <= 0 || separatorIndex >= grainKey.Length - 1)
        {
            return false;
        }

        var candidate = grainKey[..separatorIndex];
        if (!TryNormalizeServiceId(candidate, out serviceId))
        {
            return false;
        }

        rawKey = grainKey[(separatorIndex + 1)..];
        return true;
    }

    private static bool TryNormalizeServiceId(string serviceId, out string normalized)
    {
        normalized = DefaultServiceIdProvider.DefaultServiceId;
        try
        {
            normalized = ServiceIdValidator.NormalizeAndValidate(serviceId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsDefaultServiceId(string serviceId) =>
        string.Equals(serviceId, DefaultServiceIdProvider.DefaultServiceId, StringComparison.Ordinal);
}
