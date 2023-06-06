using Sekiban.Core.Validation;
using System.ComponentModel.DataAnnotations;
namespace Sekiban.Core.Exceptions;

public class SekibanValidationErrorsException : Exception, ISekibanException
{

    public IEnumerable<SekibanValidationParameterError> Errors { get; init; }
    public SekibanValidationErrorsException(IEnumerable<SekibanValidationParameterError> errors) => Errors = errors;

    public SekibanValidationErrorsException(IEnumerable<ValidationResult> validationResults) =>
        Errors = SekibanValidationParameterError.CreateFromValidationResults(validationResults);
}
