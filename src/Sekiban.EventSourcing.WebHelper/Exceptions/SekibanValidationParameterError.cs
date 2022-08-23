namespace Sekiban.EventSourcing.WebHelper.Exceptions;

public record SekibanValidationParameterError(string PropertyName, IEnumerable<string> ErrorMessages);
