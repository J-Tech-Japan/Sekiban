using Sekiban.Pure.Exceptions;
namespace Sekiban.Pure.Exceptions;

public class SekibanConfigurationException(string message)
    : ApplicationException(message), ISekibanException;