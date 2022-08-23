using Sekiban.EventSourcing.Validations;
using System.ComponentModel.DataAnnotations;
namespace Sekiban.EventSourcing.WebHelper.Exceptions;

public class SekibanValidationErrorsException : Exception
{
    public IEnumerable<SekibanValidationParameterError> Errors { get; init; }

    public SekibanValidationErrorsException(IEnumerable<SekibanValidationParameterError> errors) =>
        Errors = errors;
    public SekibanValidationErrorsException(IEnumerable<ValidationResult> validationResults)
    {
        var list = validationResults.ToList();
        var errors = list.Select(m => m.MemberNames.FirstOrDefault() ?? string.Empty).Distinct().ToList();
        Errors = errors.Select(
            param => new SekibanValidationParameterError(
                param,
                list.Where(m => (m.MemberNames.FirstOrDefault() ?? string.Empty) == param).Select(m => m.ErrorMessage ?? string.Empty).ToList()));
    }
}
