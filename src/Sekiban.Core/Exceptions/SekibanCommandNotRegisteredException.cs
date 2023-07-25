using Sekiban.Core.Dependency;
namespace Sekiban.Core.Exceptions;

/// <summary>
///     This exception throws when command passed to CommandExecutor is not registered as command.
///     Consider to register command to <see cref="IDependencyDefinition" />
/// </summary>
public class SekibanCommandNotRegisteredException : Exception, ISekibanException
{
    /// <summary>
    ///     Command that was not registered
    /// </summary>
    public string CommandName { get; set; }
    public SekibanCommandNotRegisteredException(string commandName) : base($"{commandName} was not registered in Dependency Definition") =>
        CommandName = commandName;
}
