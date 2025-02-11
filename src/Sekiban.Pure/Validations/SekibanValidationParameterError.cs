using System.ComponentModel.DataAnnotations;
namespace Sekiban.Pure.Validations;

/// <summary>
///     Validation Parameter Error using in Sekiban.
/// </summary>
/// <param name="PropertyName"></param>
/// <param name="ErrorMessages"></param>
public record SekibanValidationParameterError(string PropertyName, IEnumerable<string> ErrorMessages)
{
    /// <summary>
    ///     Create SekibanValidationParameterError from ValidationResults.
    /// </summary>
    /// <param name="validationResults"></param>
    /// <returns></returns>
    public static IEnumerable<SekibanValidationParameterError> CreateFromValidationResults(
        IEnumerable<ValidationResult> validationResults)
    {
        var list = validationResults.ToList();
        var errors = list.Select(m => m.MemberNames.FirstOrDefault() ?? string.Empty).Distinct().ToList();
        return errors.Select(
            param => new SekibanValidationParameterError(
                param,
                list
                    .Where(m => (m.MemberNames.FirstOrDefault() ?? string.Empty) == param)
                    .Select(m => m.ErrorMessage ?? string.Empty)
                    .ToList()));
    }
}