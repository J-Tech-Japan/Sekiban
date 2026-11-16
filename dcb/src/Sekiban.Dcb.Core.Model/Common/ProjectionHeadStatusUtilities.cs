using System.Diagnostics.CodeAnalysis;

namespace Sekiban.Dcb.Common;

/// <summary>
///     Shared helpers for projection head status APIs. Each helper returns the validated value or throws.
/// </summary>
public static class ProjectionHeadStatusUtilities
{
    /// <summary>
    ///     Returns the registered projector version.
    /// </summary>
    /// <exception cref="ArgumentException">The projector name is empty.</exception>
    /// <exception cref="InvalidOperationException">The registered version differs from <paramref name="expectedProjectorVersion" />.</exception>
    public static string ValidateProjectorVersion(
        DcbDomainTypes domainTypes,
        string projectorName,
        string? expectedProjectorVersion)
    {
        if (string.IsNullOrWhiteSpace(projectorName))
        {
            throw new ArgumentException("Projector name cannot be empty.", nameof(projectorName));
        }

        var currentProjectorVersion = domainTypes.MultiProjectorTypes.GetProjectorVersion(projectorName);
        if (!string.IsNullOrWhiteSpace(expectedProjectorVersion)
            && !string.Equals(currentProjectorVersion, expectedProjectorVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Projector version mismatch for '{projectorName}'. Expected '{expectedProjectorVersion}', but registered version is '{currentProjectorVersion}'.");
        }

        return currentProjectorVersion;
    }

    /// <summary>
    ///     Returns the value of the public static <c>MultiProjectorName</c> property of <paramref name="projectorType" />.
    /// </summary>
    /// <exception cref="InvalidOperationException">The type has no valid <c>MultiProjectorName</c>.</exception>
    [UnconditionalSuppressMessage(
        "AOT",
        "IL2070",
        Justification = "Projector types are registered runtime types that expose the public static MultiProjectorName property.")]
    public static string ResolveProjectorName(Type projectorType)
    {
        ArgumentNullException.ThrowIfNull(projectorType);

        var projectorNameProperty = projectorType.GetProperty("MultiProjectorName");
        if (projectorNameProperty == null)
        {
            throw new InvalidOperationException(
                $"Projector type {projectorType.Name} does not have MultiProjectorName property");
        }

        var projectorName = projectorNameProperty.GetValue(null) as string;
        if (string.IsNullOrWhiteSpace(projectorName))
        {
            throw new InvalidOperationException(
                $"Projector type {projectorType.Name} has invalid MultiProjectorName");
        }

        return projectorName;
    }

    /// <summary>
    ///     Returns <paramref name="requestedProjectorName" /> when the projection reported no name or the same name.
    /// </summary>
    /// <exception cref="ArgumentException">The requested name is empty.</exception>
    /// <exception cref="InvalidOperationException">The projection reported a different name.</exception>
    public static string EnsureProjectorNameConsistency(
        string requestedProjectorName,
        string? actualProjectorName)
    {
        if (string.IsNullOrWhiteSpace(requestedProjectorName))
        {
            throw new ArgumentException("Projector name cannot be empty.", nameof(requestedProjectorName));
        }

        if (!string.IsNullOrWhiteSpace(actualProjectorName)
            && !string.Equals(requestedProjectorName, actualProjectorName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Projector name mismatch. Requested '{requestedProjectorName}', but projection returned '{actualProjectorName}'.");
        }

        return requestedProjectorName;
    }

    /// <summary>
    ///     Returns <paramref name="registeredProjectorVersion" /> when the projection reported no version or the same one.
    /// </summary>
    /// <exception cref="InvalidOperationException">The projection reported a different version.</exception>
    public static string EnsureProjectorVersionConsistency(
        string registeredProjectorVersion,
        string? actualProjectorVersion)
    {
        if (!string.IsNullOrWhiteSpace(actualProjectorVersion)
            && !string.Equals(registeredProjectorVersion, actualProjectorVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Projector version mismatch. Registered '{registeredProjectorVersion}', but projection returned '{actualProjectorVersion}'.");
        }

        return registeredProjectorVersion;
    }

    public static string? NormalizeSortableUniqueId(string? sortableUniqueId) =>
        string.IsNullOrWhiteSpace(sortableUniqueId) ? null : sortableUniqueId;
}
