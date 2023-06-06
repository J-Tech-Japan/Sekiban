namespace Sekiban.Core.Exceptions;

public class SekibanCommandNotRegisteredException : Exception, ISekibanException
{

    public string CommandName { get; set; }
    public SekibanCommandNotRegisteredException(string commandName) => CommandName = commandName;
}
