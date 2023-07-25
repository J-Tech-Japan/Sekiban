using Sekiban.Core.Validation;
using System.ComponentModel.DataAnnotations;
namespace Sekiban.Core.Exceptions;
/// <summary>
/// This exception is thrown when the validation errors are found.
/// </summary>
public class SekibanValidationErrorsException : Exception, ISekibanException
{

    public IEnumerable<SekibanValidationParameterError> Errors { get; init; }
    public SekibanValidationErrorsException(IEnumerable<SekibanValidationParameterError> errors) => Errors = errors;

    public SekibanValidationErrorsException(IEnumerable<ValidationResult> validationResults) =>
        Errors = SekibanValidationParameterError.CreateFromValidationResults(validationResults);
}
