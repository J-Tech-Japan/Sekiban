namespace Sekiban.EventSourcing.Shared.Exceptions;

public class JJAggregateCommandNotRegisteredException : Exception, IJJException
{
    public string CommandName { get; set; }

    public JJAggregateCommandNotRegisteredException(string commandName) =>
        CommandName = commandName;
}
