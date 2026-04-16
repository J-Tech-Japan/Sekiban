using Sekiban.Dcb.Orleans.ServiceId;
using Sekiban.Dcb.ServiceId;

namespace Sekiban.Dcb.MaterializedView.Orleans;

public static class MvGrainKey
{
    private const string Prefix = "mv::";
    private const string VersionSeparator = "::v::";

    public static string Build(string serviceId, string viewName, int viewVersion) =>
        ServiceIdGrainKey.Build(serviceId, BuildRaw(viewName, viewVersion));

    public static string BuildRaw(string viewName, int viewVersion)
    {
        if (string.IsNullOrWhiteSpace(viewName))
        {
            throw new ArgumentException("View name cannot be empty.", nameof(viewName));
        }

        return $"{Prefix}{viewName}{VersionSeparator}{viewVersion}";
    }

    public static (string ServiceId, string ViewName, int ViewVersion) Parse(string grainKey)
    {
        var (serviceId, rawKey) = ServiceIdGrainKey.Parse(grainKey);
        if (!TryParseRaw(rawKey, out var viewName, out var viewVersion))
        {
            throw new ArgumentException($"Invalid materialized view grain key '{grainKey}'.", nameof(grainKey));
        }

        return (serviceId, viewName, viewVersion);
    }

    public static bool TryParse(string grainKey, out string serviceId, out string viewName, out int viewVersion)
    {
        serviceId = DefaultServiceIdProvider.DefaultServiceId;
        viewName = string.Empty;
        viewVersion = 0;

        if (string.IsNullOrWhiteSpace(grainKey))
        {
            return false;
        }

        var parsed = ServiceIdGrainKey.Parse(grainKey);
        if (!TryParseRaw(parsed.RawKey, out viewName, out viewVersion))
        {
            return false;
        }

        serviceId = parsed.ServiceId;
        return true;
    }

    public static bool TryParseRaw(string rawKey, out string viewName, out int viewVersion)
    {
        viewName = string.Empty;
        viewVersion = 0;

        if (string.IsNullOrWhiteSpace(rawKey) || !rawKey.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var versionSeparatorIndex = rawKey.LastIndexOf(VersionSeparator, StringComparison.Ordinal);
        if (versionSeparatorIndex <= Prefix.Length)
        {
            return false;
        }

        viewName = rawKey[Prefix.Length..versionSeparatorIndex];
        if (string.IsNullOrWhiteSpace(viewName))
        {
            return false;
        }

        var versionSegment = rawKey[(versionSeparatorIndex + VersionSeparator.Length)..];
        return int.TryParse(versionSegment, out viewVersion);
    }
}
