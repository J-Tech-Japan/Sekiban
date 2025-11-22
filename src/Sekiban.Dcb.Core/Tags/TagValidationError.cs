namespace Sekiban.Dcb.Tags;

/// <summary>
///     Represents a single tag validation error
/// </summary>
public class TagValidationError
{
    public string TagString { get; }
    public string Message { get; }
    public TagValidationErrorType ErrorType { get; }

    public TagValidationError(string tagString, string message, TagValidationErrorType errorType)
    {
        TagString = tagString;
        Message = message;
        ErrorType = errorType;
    }
}
