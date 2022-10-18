using Sekiban.Core.Validation;
using System.ComponentModel.DataAnnotations;
namespace Sekiban.Addon.Web.Exceptions;

public class SekibanValidationErrorsException : Exception
{
    public IEnumerable<SekibanValidationParameterError> Errors { get; init; }

    public SekibanValidationErrorsException(IEnumerable<SekibanValidationParameterError> errors)
    {
        Errors = errors;
    }
    public SekibanValidationErrorsException(IEnumerable<ValidationResult> validationResults)
    {
        Errors = SekibanValidationParameterError.CreateFromValidationResults(validationResults);
    }
}
