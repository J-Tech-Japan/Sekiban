namespace Sekiban.Core.Exceptions;

/// <summary>
///     This exception is thrown when the query paging is invalid.
/// </summary>
public class SekibanQueryPagingError : Exception, ISekibanException;