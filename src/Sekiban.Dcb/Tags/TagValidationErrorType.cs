namespace Sekiban.Dcb.Tags;

/// <summary>
/// Types of tag validation errors
/// </summary>
public enum TagValidationErrorType
{
    InvalidFormat,
    InvalidCharactersInGroup,
    InvalidCharactersInContent,
    EmptyGroup,
    EmptyContent,
    GroupTooLong,
    ContentTooLong
}