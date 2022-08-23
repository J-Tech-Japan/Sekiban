using Sekiban.EventSourcing.Validations;
using System.ComponentModel.DataAnnotations;
namespace Sekiban.EventSourcing.WebHelper.Exceptions;

public class SekibanValidationErrorsException : Exception
{
    public IEnumerable<SekibanValidationParameterError> Errors { get; init; }

    public SekibanValidationErrorsException(IEnumerable<SekibanValidationParameterError> errors) =>
        Errors = errors;
    public SekibanValidationErrorsException(IEnumerable<ValidationResult> validationResults) =>
        Errors = SekibanValidationParameterError.CreateFromValidationResults(validationResults);
}
