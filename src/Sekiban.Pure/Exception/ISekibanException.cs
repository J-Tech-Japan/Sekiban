namespace Sekiban.Pure.Exception;

/// <summary>
///     BaseType for the Sekiban Exception
/// </summary>
public interface ISekibanException;
public class SekibanQueryPagingError : System.Exception, ISekibanException;
