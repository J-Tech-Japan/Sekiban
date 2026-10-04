extern alias WithoutResultFacade;
using Orleans;
using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.TagConsistencyFence;
using WithoutCommands = WithoutResultFacade::Sekiban.Dcb.Commands;
using WithoutExecutor = WithoutResultFacade::Sekiban.Dcb.Orleans.OrleansDcbExecutor;

namespace Sekiban.Dcb.Orleans.Tests;

// One adapter keeps the assertions and race protocol identical for both facades.
internal sealed class PerCommandFenceFacade
{
    private readonly object _executor;
    internal PerCommandFenceFacade(bool withoutResult, IClusterClient client, IEventStore store,
        TagConsistencyFenceMode? global = null)
    {
        var domain = G20Shared.BuildDomain();
        var fence = global.HasValue ? new TagConsistencyFenceOptions { Mode = global.Value } : null;
        _executor = withoutResult
            ? fence is null ? new WithoutExecutor(client, store, domain)
                : new WithoutExecutor(client, store, domain, tagConsistencyFenceOptions: fence)
            : fence is null ? new OrleansDcbExecutor(client, store, domain)
                : new OrleansDcbExecutor(client, store, domain, tagConsistencyFenceOptions: fence);
    }

    internal Task<ResultBox<ExecutionResult>> Execute(Guid id, string value, CommandExecutionOptions? options,
        bool suppliedHandler = false, Action? handlerEntered = null)
    {
        if (_executor is IConditionalCommandExecutor with)
        {
            var command = new G22UpsertCommand(id, value);
            return suppliedHandler
                ? with.ExecuteAsync(command, (cmd, ctx) =>
                {
                    handlerEntered?.Invoke();
                    return G22UpsertCommand.HandleAsync(cmd, ctx);
                }, options!)
                : with.ExecuteAsync(command, options!);
        }
        var without = (WithoutCommands.IConditionalCommandExecutor)_executor;
        var other = new WithoutCommand(id, value);
        // Invoke before entering the async adapter: a synchronous rejection escapes to the test caller.
        var task = suppliedHandler
            ? without.ExecuteAsync(other, (cmd, ctx) =>
            {
                handlerEntered?.Invoke();
                return WithoutCommand.HandleAsync(cmd, ctx);
            }, options!)
            : without.ExecuteAsync(other, options!);
        return Capture(task);
    }

    private static async Task<ResultBox<ExecutionResult>> Capture(Task<ExecutionResult> task)
    {
        try { return ResultBox.FromValue(await task); }
        catch (Exception exception) { return ResultBox.Error<ExecutionResult>(exception); }
    }

    internal sealed record WithoutCommand(Guid Id, string Value) : WithoutCommands.ICommandWithHandler<WithoutCommand>
    {
        public static async Task<EventOrNone> HandleAsync(WithoutCommand command, WithoutCommands.ICommandContext context)
        {
            var tag = new G22ReservationTag(command.Id);
            await context.GetStateAsync<G22ReservationProjector>(tag);
            return EventOrNone.EventWithTags(new G22Upserted(command.Id, command.Value), tag).GetValue();
        }
    }
}
