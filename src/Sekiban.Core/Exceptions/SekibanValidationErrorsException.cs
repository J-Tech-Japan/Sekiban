using Sekiban.Core.Validation;
using System.ComponentModel.DataAnnotations;
namespace Sekiban.Core.Exceptions;

/// <summary>
///     This exception is thrown when the validation errors are found.
/// </summary>
public class SekibanValidationErrorsException(IEnumerable<SekibanValidationParameterError> errors) : Exception, ISekibanException
{

    public IEnumerable<SekibanValidationParameterError> Errors { get; init; } = errors;

    public SekibanValidationErrorsException(IEnumerable<ValidationResult> validationResults) : this(
        SekibanValidationParameterError.CreateFromValidationResults(validationResults))
    {
    }
}
