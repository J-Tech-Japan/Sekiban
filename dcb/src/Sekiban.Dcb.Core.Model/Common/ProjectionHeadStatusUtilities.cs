using System.Diagnostics.CodeAnalysis;
using ResultBoxes;

namespace Sekiban.Dcb.Common;

/// <summary>
///     Shared helpers for projection head status APIs.
/// </summary>
public static class ProjectionHeadStatusUtilities
{
    public static ResultBox<string> ValidateProjectorVersion(
        DcbDomainTypes domainTypes,
        string projectorName,
        string? expectedProjectorVersion)
    {
        if (string.IsNullOrWhiteSpace(projectorName))
        {
            return ResultBox.Error<string>(new ArgumentException("Projector name cannot be empty.", nameof(projectorName)));
        }

        var projectorVersionResult = domainTypes.MultiProjectorTypes.GetProjectorVersion(projectorName);
        if (!projectorVersionResult.IsSuccess)
        {
            return ResultBox.Error<string>(projectorVersionResult.GetException());
        }

        var currentProjectorVersion = projectorVersionResult.GetValue();
        if (!string.IsNullOrWhiteSpace(expectedProjectorVersion)
            && !string.Equals(currentProjectorVersion, expectedProjectorVersion, StringComparison.Ordinal))
        {
            return ResultBox.Error<string>(
                new InvalidOperationException(
                    $"Projector version mismatch for '{projectorName}'. Expected '{expectedProjectorVersion}', but registered version is '{currentProjectorVersion}'."));
        }

        return ResultBox.FromValue(currentProjectorVersion);
    }

    [UnconditionalSuppressMessage(
        "AOT",
        "IL2075",
        Justification = "Projector types are registered runtime types that expose the public static MultiProjectorName property.")]
    public static ResultBox<string> ResolveProjectorName(ResultBox<Type> projectorTypeResult)
    {
        if (!projectorTypeResult.IsSuccess)
        {
            return ResultBox.Error<string>(projectorTypeResult.GetException());
        }

        var projectorType = projectorTypeResult.GetValue();
        var projectorNameProperty = projectorType.GetProperty("MultiProjectorName");
        if (projectorNameProperty == null)
        {
            return ResultBox.Error<string>(
                new InvalidOperationException(
                    $"Projector type {projectorType.Name} does not have MultiProjectorName property"));
        }

        var projectorName = projectorNameProperty.GetValue(null) as string;
        if (string.IsNullOrWhiteSpace(projectorName))
        {
            return ResultBox.Error<string>(
                new InvalidOperationException(
                    $"Projector type {projectorType.Name} has invalid MultiProjectorName"));
        }

        return ResultBox.FromValue(projectorName);
    }

    public static ResultBox<string> EnsureProjectorNameConsistency(
        string requestedProjectorName,
        string? actualProjectorName)
    {
        if (string.IsNullOrWhiteSpace(requestedProjectorName))
        {
            return ResultBox.Error<string>(
                new ArgumentException("Projector name cannot be empty.", nameof(requestedProjectorName)));
        }

        if (string.IsNullOrWhiteSpace(actualProjectorName))
        {
            return ResultBox.FromValue(requestedProjectorName);
        }

        if (!string.Equals(requestedProjectorName, actualProjectorName, StringComparison.Ordinal))
        {
            return ResultBox.Error<string>(
                new InvalidOperationException(
                    $"Projector name mismatch. Requested '{requestedProjectorName}', but projection returned '{actualProjectorName}'."));
        }

        return ResultBox.FromValue(requestedProjectorName);
    }

    public static ResultBox<string> EnsureProjectorVersionConsistency(
        string registeredProjectorVersion,
        string? actualProjectorVersion)
    {
        if (string.IsNullOrWhiteSpace(actualProjectorVersion))
        {
            return ResultBox.FromValue(registeredProjectorVersion);
        }

        if (!string.Equals(registeredProjectorVersion, actualProjectorVersion, StringComparison.Ordinal))
        {
            return ResultBox.Error<string>(
                new InvalidOperationException(
                    $"Projector version mismatch. Registered '{registeredProjectorVersion}', but projection returned '{actualProjectorVersion}'."));
        }

        return ResultBox.FromValue(registeredProjectorVersion);
    }

    public static string? NormalizeSortableUniqueId(string? sortableUniqueId) =>
        string.IsNullOrWhiteSpace(sortableUniqueId) ? null : sortableUniqueId;
}
