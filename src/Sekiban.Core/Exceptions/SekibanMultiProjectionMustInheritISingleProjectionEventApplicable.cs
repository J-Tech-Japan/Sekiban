namespace Sekiban.Core.Exceptions;

/// <summary>
///     This exception is thrown when the multi projection did not inherit ISingleProjectionEventApplicable.
/// </summary>
public class SekibanMultiProjectionMustInheritISingleProjectionEventApplicable : Exception, ISekibanException;
