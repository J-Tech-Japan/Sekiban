using System.ComponentModel.DataAnnotations;
using System.Text;
namespace Sekiban.Dcb.Validation;

/// <summary>
///     Exception thrown when Sekiban object validation fails.
///     Inherits from ValidationException to be compatible with ASP.NET Core exception handling.
/// </summary>
public class SekibanValidationException : ValidationException
{
    public IReadOnlyList<SekibanValidationError> Errors { get; }

    public SekibanValidationException(IEnumerable<SekibanValidationError> errors)
        : base(CreateValidationResult(errors), null, null)
    {
        Errors = errors.ToList().AsReadOnly();
    }

    public SekibanValidationException(SekibanValidationError error) : this(new[] { error })
    {
    }

    private static ValidationResult CreateValidationResult(IEnumerable<SekibanValidationError> errors)
    {
        var errorList = errors.ToList();
        if (errorList.Count == 0)
            return new ValidationResult("Validation failed");

        var sb = new StringBuilder();
        sb.AppendLine($"Validation failed with {errorList.Count} error(s):");
        foreach (var error in errorList)
        {
            sb.AppendLine($"  - {error}");
        }

        // Collect all property names for ASP.NET Core validation problem details
        var memberNames = errorList.Select(e => e.PropertyName).Distinct();

        return new ValidationResult(sb.ToString().TrimEnd(), memberNames);
    }
}
