using System.Reflection;
using DcbLib.Commands;
using DcbLib.Events;
using ResultBoxes;

namespace DcbLib.Domains;

/// <summary>
/// Simple implementation of ICommandTypes that manages command handlers
/// </summary>
public class SimpleCommandTypes : ICommandTypes
{
    private readonly Dictionary<Type, object> _commandHandlers;
    
    public SimpleCommandTypes()
    {
        _commandHandlers = new Dictionary<Type, object>();
    }
    
    /// <summary>
    /// Register a command handler
    /// </summary>
    public void RegisterHandler<TCommand>(ICommandHandler<TCommand> handler) where TCommand : ICommand
    {
        _commandHandlers[typeof(TCommand)] = handler;
    }
    
    /// <summary>
    /// Register a command handler using a function
    /// </summary>
    public void RegisterHandler<TCommand>(Func<TCommand, ICommandContext, Task<ResultBox<EventOrNone>>> handler) 
        where TCommand : ICommand
    {
        _commandHandlers[typeof(TCommand)] = handler;
    }
    
    public Func<TCommand, ICommandContext, Task<ResultBox<EventOrNone>>>? GetActionForCommand<TCommand>(TCommand command) 
        where TCommand : ICommand
    {
        var commandType = typeof(TCommand);
        
        if (_commandHandlers.TryGetValue(commandType, out var handler))
        {
            // If it's already a function, return it
            if (handler is Func<TCommand, ICommandContext, Task<ResultBox<EventOrNone>>> func)
            {
                return func;
            }
            
            // If it's an ICommandHandler<TCommand>, convert it to a function
            if (handler is ICommandHandler<TCommand> commandHandler)
            {
                return (cmd, ctx) => commandHandler.HandleAsync(cmd, ctx);
            }
        }
        
        return null;
    }
}