using System.ComponentModel.DataAnnotations;

namespace Sekiban.Core.Validation;

public record SekibanValidationParameterError(string PropertyName, IEnumerable<string> ErrorMessages)
{
    public static IEnumerable<SekibanValidationParameterError> CreateFromValidationResults(
        IEnumerable<ValidationResult> validationResults)
    {
        var list = validationResults.ToList();
        var errors = list.Select(m => m.MemberNames.FirstOrDefault() ?? string.Empty).Distinct().ToList();
        return errors.Select(
            param => new SekibanValidationParameterError(
                param,
                list.Where(m => (m.MemberNames.FirstOrDefault() ?? string.Empty) == param)
                    .Select(m => m.ErrorMessage ?? string.Empty).ToList()));
    }
}
