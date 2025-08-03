using DcbLib.Commands;
using DcbLib.Events;
using ResultBoxes;

namespace DcbLib.Domains;

/// <summary>
/// Interface for managing command types in the domain
/// </summary>
public interface ICommandTypes
{
    /// <summary>
    /// Gets the action to handle a command
    /// </summary>
    Func<TCommand, ICommandContext, Task<ResultBox<EventOrNone>>>? GetActionForCommand<TCommand>(TCommand command) 
        where TCommand : ICommand;
}