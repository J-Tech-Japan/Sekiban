using System.Text.RegularExpressions;

namespace Sekiban.Dcb.ServiceId;

public static class ServiceIdValidator
{
    private static readonly Regex Pattern = new(
        "^[a-z0-9-]{1,64}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string NormalizeAndValidate(string serviceId)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
        {
            throw new ArgumentException("ServiceId must not be null or whitespace.", nameof(serviceId));
        }

        if (!string.Equals(serviceId, serviceId.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("ServiceId must not include leading or trailing whitespace.", nameof(serviceId));
        }

        var normalized = serviceId.ToLowerInvariant();

        if (!Pattern.IsMatch(normalized))
        {
            throw new ArgumentException(
                "ServiceId must match ^[a-z0-9-]{1,64}$ and must not contain '|', '/', or whitespace.",
                nameof(serviceId));
        }

        return normalized;
    }
}
