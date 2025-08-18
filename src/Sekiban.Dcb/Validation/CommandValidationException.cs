using System.Text;
namespace Sekiban.Dcb.Validation;

/// <summary>
///     Exception thrown when command validation fails
/// </summary>
public class CommandValidationException : Exception
{
    public IReadOnlyList<CommandValidationError> Errors { get; }

    public CommandValidationException(IEnumerable<CommandValidationError> errors) : base(BuildMessage(errors)) =>
        Errors = errors.ToList().AsReadOnly();

    public CommandValidationException(CommandValidationError error) : this(new[] { error })
    {
    }

    private static string BuildMessage(IEnumerable<CommandValidationError> errors)
    {
        var errorList = errors.ToList();
        if (errorList.Count == 0)
            return "Command validation failed";

        var sb = new StringBuilder();
        sb.AppendLine($"Command validation failed with {errorList.Count} error(s):");
        foreach (var error in errorList)
        {
            sb.AppendLine($"  - {error}");
        }
        return sb.ToString();
    }
}
