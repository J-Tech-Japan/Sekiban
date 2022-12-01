namespace Sekiban.Core.Exceptions;

public class SekibanCommandNotRegisteredException : Exception, ISekibanException
{
    public SekibanCommandNotRegisteredException(string commandName)
    {
        CommandName = commandName;
    }

    public string CommandName { get; set; }
}
