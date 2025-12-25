using Sekiban.Dcb.Tags;
using System.Text.RegularExpressions;
namespace Sekiban.Dcb.Validation;

/// <summary>
///     Provides validation for various naming conventions in the system
/// </summary>
public static class NameValidator
{

    // Maximum length for projector names
    private const int MaxProjectorNameLength = 40;
    // Pattern for projector names: letters (uppercase and lowercase), hyphen, and underscore
    private static readonly Regex ProjectorNamePattern = new(@"^[a-zA-Z\-_]+$", RegexOptions.Compiled);

    /// <summary>
    ///     Validates a projector name
    /// </summary>
    /// <param name="projectorName">The projector name to validate</param>
    /// <returns>True if valid, false otherwise</returns>
    public static bool IsValidProjectorName(string projectorName)
    {
        if (string.IsNullOrWhiteSpace(projectorName))
            return false;

        if (projectorName.Length > MaxProjectorNameLength)
            return false;

        return ProjectorNamePattern.IsMatch(projectorName);
    }

    /// <summary>
    ///     Validates a projector name and throws an exception if invalid
    /// </summary>
    /// <param name="projectorName">The projector name to validate</param>
    /// <exception cref="ArgumentException">Thrown when the projector name is invalid</exception>
    public static void ValidateProjectorNameAndThrow(string projectorName)
    {
        if (string.IsNullOrWhiteSpace(projectorName))
        {
            throw new ArgumentException("Projector name cannot be null or empty");
        }

        if (projectorName.Length > MaxProjectorNameLength)
        {
            throw new ArgumentException(
                $"Projector name '{projectorName}' exceeds maximum length of {MaxProjectorNameLength} characters (actual: {projectorName.Length})");
        }

        if (!ProjectorNamePattern.IsMatch(projectorName))
        {
            throw new ArgumentException(
                $"Projector name '{projectorName}' contains invalid characters. Only letters, hyphen (-), and underscore (_) are allowed");
        }
    }

    /// <summary>
    ///     Validates a tag group name using the existing tag validation rules
    /// </summary>
    /// <param name="tagGroupName">The tag group name to validate</param>
    /// <exception cref="ArgumentException">Thrown when the tag group name is invalid</exception>
    public static void ValidateTagGroupNameAndThrow(string tagGroupName)
    {
        if (string.IsNullOrWhiteSpace(tagGroupName))
        {
            throw new ArgumentException("Tag group name cannot be null or empty");
        }

        // Check for colon which is not allowed in group names as it's used as delimiter
        if (tagGroupName.Contains(':'))
        {
            throw new ArgumentException(
                $"Tag group name '{tagGroupName}' contains invalid characters. Colon (:) is not allowed as it is used as a delimiter");
        }

        // Create a dummy tag to validate the group name using existing validation
        var dummyTag = new DummyTag(tagGroupName, "dummy");
        var errors = TagValidator.ValidateTag(dummyTag);

        // Filter for group-related errors only
        var groupErrors = errors
            .Where(e => e.ErrorType == TagValidationErrorType.EmptyGroup ||
                e.ErrorType == TagValidationErrorType.InvalidCharactersInGroup ||
                e.ErrorType == TagValidationErrorType.GroupTooLong)
            .ToList();

        if (groupErrors.Any())
        {
            throw new ArgumentException($"Invalid tag group name '{tagGroupName}': {groupErrors.First().Message}");
        }
    }

    // Helper class for validation
    private class DummyTag : ITag
    {
        private readonly string _content;
        private readonly string _group;

        public DummyTag(string group, string content)
        {
            _group = group;
            _content = content;
        }

        public bool IsConsistencyTag() => false;
        public string GetTagGroup() => _group;
        public string GetTagContent() => _content;
        public string GetTag() => $"{_group}:{_content}";
    }
}
