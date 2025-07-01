using System.Text.RegularExpressions;

namespace Sekiban.Pure.Dapr.Utils;

/// <summary>
/// Helper class for ensuring names are compliant with Kubernetes RFC 1123 subdomain requirements.
/// </summary>
public static class KubernetesNameHelper
{
    private static readonly Regex InvalidCharactersRegex = new(@"[^a-z0-9-]", RegexOptions.Compiled);
    private static readonly Regex MultipleHyphensRegex = new(@"-+", RegexOptions.Compiled);
    private static readonly Regex StartEndTrimRegex = new(@"^[^a-z0-9]+|[^a-z0-9]+$", RegexOptions.Compiled);
    
    /// <summary>
    /// Sanitizes a string to be compliant with Kubernetes naming requirements.
    /// - Must be lowercase
    /// - Must consist of alphanumeric characters or '-'
    /// - Must start and end with an alphanumeric character
    /// - Must be no more than 63 characters
    /// </summary>
    /// <param name="input">The input string to sanitize</param>
    /// <returns>A Kubernetes-compliant name</returns>
    public static string SanitizeForKubernetes(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("Input cannot be null or whitespace", nameof(input));
        }
        
        // Convert to lowercase
        var sanitized = input.ToLowerInvariant();
        
        // Replace invalid characters with hyphens
        sanitized = InvalidCharactersRegex.Replace(sanitized, "-");
        
        // Replace multiple consecutive hyphens with a single hyphen
        sanitized = MultipleHyphensRegex.Replace(sanitized, "-");
        
        // Remove leading and trailing non-alphanumeric characters
        sanitized = StartEndTrimRegex.Replace(sanitized, "");
        
        // Ensure we have something left
        if (string.IsNullOrEmpty(sanitized))
        {
            sanitized = "default";
        }
        
        // Truncate if needed (Kubernetes limit is 63 characters)
        if (sanitized.Length > 63)
        {
            sanitized = sanitized.Substring(0, 63);
            // Ensure it doesn't end with a hyphen after truncation
            sanitized = sanitized.TrimEnd('-');
        }
        
        return sanitized;
    }
    
    /// <summary>
    /// Validates if a name is Kubernetes-compliant.
    /// </summary>
    /// <param name="name">The name to validate</param>
    /// <returns>True if the name is valid, false otherwise</returns>
    public static bool IsValidKubernetesName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 63)
        {
            return false;
        }
        
        // Must be lowercase
        if (name != name.ToLowerInvariant())
        {
            return false;
        }
        
        // Must match the pattern: lowercase alphanumeric and hyphens, 
        // starting and ending with alphanumeric
        return Regex.IsMatch(name, @"^[a-z0-9]([a-z0-9-]*[a-z0-9])?$");
    }
}