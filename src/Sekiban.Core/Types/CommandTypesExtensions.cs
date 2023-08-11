using Sekiban.Core.Command;
namespace Sekiban.Core.Types;

/// <summary>
///     Command Types Extensions.
/// </summary>
public static class CommandTypesExtensions
{
    /// <summary>
    ///     Check whether the type is a command type.
    /// </summary>
    /// <param name="commandType"></param>
    /// <returns></returns>
    public static bool IsCommandType(this Type commandType) => commandType.DoesImplementingFromGenericInterfaceType(typeof(ICommand<>));

    /// <summary>
    ///     Check whether the type is a command handler type.
    /// </summary>
    /// <param name="eventPayloadType"></param>
    /// <returns></returns>
    public static bool IsCommandHandlerType(this Type eventPayloadType) =>
        eventPayloadType.DoesImplementingFromGenericInterfaceType(typeof(ICommandHandlerCommon<,>));
    /// <summary>
    ///     Get aggregate payload type from command handler type.
    /// </summary>
    /// <param name="commandHandlerType"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public static Type GetAggregatePayloadTypeFromCommandHandlerType(this Type commandHandlerType)
    {
        if (commandHandlerType.IsCommandHandlerType())
        {
            var baseType = commandHandlerType.GetImplementingFromGenericInterfaceType(typeof(ICommandHandlerCommon<,>));
            return baseType.GetGenericArguments()[0];
        }

        throw new ArgumentException("Command type is not a command type", commandHandlerType.Name);
    }

    /// <summary>
    ///     Get command type from command handler type.
    /// </summary>
    /// <param name="commandHandlerType"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public static Type GetCommandTypeFromCommandHandlerType(this Type commandHandlerType)
    {
        if (commandHandlerType.IsCommandHandlerType())
        {
            var baseType = commandHandlerType.GetImplementingFromGenericInterfaceType(typeof(ICommandHandlerCommon<,>));
            return baseType.GetGenericArguments()[1];
        }

        throw new ArgumentException("Command type is not a command type", commandHandlerType.Name);
    }

    /// <summary>
    ///     Get aggregate payload type from command type.
    /// </summary>
    /// <param name="commandType"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public static Type GetAggregatePayloadTypeFromCommandType(this Type commandType)
    {
        if (commandType.IsCommandType())
        {
            var baseType = commandType.GetImplementingFromGenericInterfaceType(typeof(ICommand<>));
            return baseType.GetGenericArguments()[0];
        }

        throw new ArgumentException("Command type is not a command type", commandType.Name);
    }
}
