using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.Commands;

/// <summary>
///     Command handler variant that returns raw <see cref="EventOrNone"/> instead of <see cref="ResultBoxes.ResultBox"/>.
///     Useful in exception-driven flows handled by <see cref="ISekibanExecutorWithoutResult"/>.
/// </summary>
/// <typeparam name="TCommand">The command type this handler processes.</typeparam>
public interface ICommandHandlerWithoutResult<in TCommand> where TCommand : ICommand
{
    static abstract Task<EventOrNone> HandleAsync(TCommand command, ICommandContext context);
}
