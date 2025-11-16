using System.Text.RegularExpressions;
namespace Sekiban.Dcb.Tags;

/// <summary>
///     Provides validation functionality for tags
/// </summary>
public static class TagValidator
{

    // Maximum length constraints
    private const int MaxTagGroupLength = 40;
    private const int MaxTagContentLength = 80;
    // Pattern for valid characters: letters (uppercase and lowercase), numbers, hyphen, underscore, and dot
    // Note: Numbers are allowed to support GUID-based identifiers
    private static readonly Regex ValidCharactersPattern = new(@"^[a-zA-Z0-9\-_.]+$", RegexOptions.Compiled);

    /// <summary>
    ///     Validates a single tag and returns validation errors if any
    /// </summary>
    /// <param name="tag">The tag to validate</param>
    /// <returns>A list of validation errors, empty if the tag is valid</returns>
    public static List<TagValidationError> ValidateTag(ITag tag)
    {
        var errors = new List<TagValidationError>();
        var tagString = tag.GetTag();

        // Check if tag contains the delimiter
        if (!tagString.Contains(':'))
        {
            errors.Add(
                new TagValidationError(
                    tagString,
                    $"Tag '{tagString}' is missing the ':' delimiter between group and content",
                    TagValidationErrorType.InvalidFormat));
            return errors; // Return early as we can't validate group/content without proper format
        }

        var parts = tagString.Split(':', 2);
        var group = parts[0];
        var content = parts.Length > 1 ? parts[1] : "";

        // Validate group
        if (string.IsNullOrEmpty(group))
        {
            errors.Add(
                new TagValidationError(
                    tagString,
                    $"Tag '{tagString}' has an empty group",
                    TagValidationErrorType.EmptyGroup));
        } else
        {
            if (!ValidCharactersPattern.IsMatch(group))
            {
                errors.Add(
                    new TagValidationError(
                        tagString,
                        $"Tag group '{group}' contains invalid characters. Only letters, numbers, hyphen (-), underscore (_), and dot (.) are allowed",
                        TagValidationErrorType.InvalidCharactersInGroup));
            }

            if (group.Length > MaxTagGroupLength)
            {
                errors.Add(
                    new TagValidationError(
                        tagString,
                        $"Tag group '{group}' exceeds maximum length of {MaxTagGroupLength} characters (actual: {group.Length})",
                        TagValidationErrorType.GroupTooLong));
            }
        }

        // Validate content
        if (string.IsNullOrEmpty(content))
        {
            errors.Add(
                new TagValidationError(
                    tagString,
                    $"Tag '{tagString}' has empty content",
                    TagValidationErrorType.EmptyContent));
        } else
        {
            if (!ValidCharactersPattern.IsMatch(content))
            {
                errors.Add(
                    new TagValidationError(
                        tagString,
                        $"Tag content '{content}' contains invalid characters. Only letters, numbers, hyphen (-), underscore (_), and dot (.) are allowed",
                        TagValidationErrorType.InvalidCharactersInContent));
            }

            if (content.Length > MaxTagContentLength)
            {
                errors.Add(
                    new TagValidationError(
                        tagString,
                        $"Tag content '{content}' exceeds maximum length of {MaxTagContentLength} characters (actual: {content.Length})",
                        TagValidationErrorType.ContentTooLong));
            }
        }

        return errors;
    }

    /// <summary>
    ///     Validates multiple tags and returns all validation errors
    /// </summary>
    /// <param name="tags">The tags to validate</param>
    /// <returns>A list of all validation errors from all tags</returns>
    public static List<TagValidationError> ValidateTags(IEnumerable<ITag> tags)
    {
        var allErrors = new List<TagValidationError>();

        foreach (var tag in tags)
        {
            var errors = ValidateTag(tag);
            allErrors.AddRange(errors);
        }

        return allErrors;
    }

    /// <summary>
    ///     Validates multiple tags and throws an exception if any validation errors are found
    /// </summary>
    /// <param name="tags">The tags to validate</param>
    /// <exception cref="TagValidationErrorsException">Thrown when validation errors are found</exception>
    public static void ValidateTagsAndThrow(IEnumerable<ITag> tags)
    {
        var errors = ValidateTags(tags);
        if (errors.Count > 0)
        {
            throw new TagValidationErrorsException(errors);
        }
    }

    /// <summary>
    ///     Validates a single tag and throws an exception if validation errors are found
    /// </summary>
    /// <param name="tag">The tag to validate</param>
    /// <exception cref="TagValidationErrorsException">Thrown when validation errors are found</exception>
    public static void ValidateTagAndThrow(ITag tag)
    {
        var errors = ValidateTag(tag);
        if (errors.Count > 0)
        {
            throw new TagValidationErrorsException(errors);
        }
    }
}
