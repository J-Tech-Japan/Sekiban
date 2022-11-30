using Sekiban.Core.Command;
namespace Sekiban.Core.Types;

public static class CommandTypesExtensions
{
    public static bool IsCommandType(this Type commandType) =>
        commandType.DoesImplementingFromGenericInterfaceType(typeof(ICommand<>)); 

    public static bool IsCommandHandlerType(this Type eventPayloadType) =>
        eventPayloadType.DoesImplementingFromGenericInterfaceType(typeof(ICommandHandler<,>));
  
    public static Type GetAggregatePayloadTypeFromCommandHandlerType(this Type commandHandlerType)
    {
        if (commandHandlerType.IsCommandHandlerType())
        {
            var baseType = commandHandlerType.GetImplementingFromGenericInterfaceType(typeof(ICommandHandler<,>));
            return baseType.GetGenericArguments()[0];
        }
        throw new ArgumentException("Command type is not a command type", commandHandlerType.Name);
    }
    public static Type GetCommandTypeFromCommandHandlerType(this Type commandHandlerType)
    {
        if (commandHandlerType.IsCommandHandlerType())
        {
            var baseType = commandHandlerType.GetImplementingFromGenericInterfaceType(typeof(ICommandHandler<,>));
            return baseType.GetGenericArguments()[1];
        }
        throw new ArgumentException("Command type is not a command type", commandHandlerType.Name);
    }
    public static Type GetAggregatePayloadTypeFromCommandType(this Type commandType)
    {
        if (commandType.IsCommandType())
        {
            var baseType = commandType.GetInheritFromGenericType(typeof(ICommand<>));
            return baseType.GetGenericArguments()[0];
        }
        throw new ArgumentException("Command type is not a command type", commandType.Name);
    }
}
