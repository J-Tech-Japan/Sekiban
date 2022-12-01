using System.ComponentModel.DataAnnotations;
using Sekiban.Core.Validation;

namespace Sekiban.Core.Exceptions;

public class SekibanValidationErrorsException : Exception, ISekibanException
{
    public SekibanValidationErrorsException(IEnumerable<SekibanValidationParameterError> errors)
    {
        Errors = errors;
    }

    public SekibanValidationErrorsException(IEnumerable<ValidationResult> validationResults)
    {
        Errors = SekibanValidationParameterError.CreateFromValidationResults(validationResults);
    }

    public IEnumerable<SekibanValidationParameterError> Errors { get; init; }
}
