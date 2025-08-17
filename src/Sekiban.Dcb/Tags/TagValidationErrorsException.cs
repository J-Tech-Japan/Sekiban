using System.Text;
namespace Sekiban.Dcb.Tags;

/// <summary>
///     Exception thrown when one or more tags fail validation
/// </summary>
public class TagValidationErrorsException : Exception
{
    public IReadOnlyList<TagValidationError> Errors { get; }

    public TagValidationErrorsException(IEnumerable<TagValidationError> errors) : base(BuildMessage(errors)) =>
        Errors = errors.ToList().AsReadOnly();

    public TagValidationErrorsException(TagValidationError error) : this(new[] { error })
    {
    }

    private static string BuildMessage(IEnumerable<TagValidationError> errors)
    {
        var errorList = errors.ToList();
        if (errorList.Count == 0)
            return "Tag validation failed";

        var sb = new StringBuilder();
        sb.AppendLine($"Tag validation failed with {errorList.Count} error(s):");
        foreach (var error in errorList)
        {
            sb.AppendLine($"  - {error.Message}");
        }
        return sb.ToString();
    }
}
