using Sekiban.Core.Command;
namespace Sekiban.Core.Types;

public static class CommandTypesExtensions
{
    public static bool IsCommandType(this Type eventPayloadType) =>
        eventPayloadType.IsCreateCommandType() || eventPayloadType.IsChangeCommandType();
    public static bool IsCreateCommandType(this Type eventPayloadType) =>
        eventPayloadType.DoesImplementingFromGenericInterfaceType(typeof(ICreateCommand<>));
    public static bool IsChangeCommandType(this Type eventPayloadType) =>
        eventPayloadType.DoesInheritFromGenericType(typeof(ChangeCommandBase<>));

    public static bool IsCreateCommandHandlerType(this Type eventPayloadType) =>
        eventPayloadType.DoesImplementingFromGenericInterfaceType(typeof(ICreateCommandHandler<,>));
    public static bool IsChangeCommandHandlerType(this Type eventPayloadType) =>
        eventPayloadType.DoesImplementingFromGenericInterfaceType(typeof(IChangeCommandHandler<,>));

    public static Type GetAggregatePayloadTypeFromCommandHandlerType(this Type commandHandlerType)
    {
        if (commandHandlerType.IsCreateCommandHandlerType())
        {
            var baseType = commandHandlerType.GetImplementingFromGenericInterfaceType(typeof(ICreateCommandHandler<,>));
            return baseType.GetGenericArguments()[0];
        }
        if (commandHandlerType.IsChangeCommandHandlerType())
        {
            var baseType = commandHandlerType.GetImplementingFromGenericInterfaceType(typeof(IChangeCommandHandler<,>));
            return baseType.GetGenericArguments()[0];
        }
        throw new ArgumentException("Command type is not a command type", commandHandlerType.Name);
    }
    public static Type GetCommandTypeFromCommandHandlerType(this Type commandHandlerType)
    {
        if (commandHandlerType.IsCreateCommandHandlerType())
        {
            var baseType = commandHandlerType.GetImplementingFromGenericInterfaceType(typeof(ICreateCommandHandler<,>));
            return baseType.GetGenericArguments()[1];
        }
        if (commandHandlerType.IsChangeCommandHandlerType())
        {
            var baseType = commandHandlerType.GetImplementingFromGenericInterfaceType(typeof(IChangeCommandHandler<,>));
            return baseType.GetGenericArguments()[1];
        }
        throw new ArgumentException("Command type is not a command type", commandHandlerType.Name);
    }
    public static Type GetAggregatePayloadTypeFromCommandType(this Type commandType)
    {
        if (commandType.IsChangeCommandType())
        {
            var baseType = commandType.GetInheritFromGenericType(typeof(ChangeCommandBase<>));
            return baseType.GetGenericArguments()[0];
        }
        if (commandType.IsCreateCommandType())
        {
            var baseType = commandType.GetImplementingFromGenericInterfaceType(typeof(ICreateCommand<>));
            return baseType.GetGenericArguments()[0];
        }
        throw new ArgumentException("Command type is not a command type", commandType.Name);
    }
}
