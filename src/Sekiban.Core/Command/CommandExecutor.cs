using Sekiban.Core.Aggregate;
using Sekiban.Core.Command.UserInformation;
using Sekiban.Core.Documents;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.History;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Shared;
using Sekiban.Core.Types;
using Sekiban.Core.Validation;
using System.Collections.Immutable;
namespace Sekiban.Core.Command;

/// <summary>
///     System use implementation of the <see cref="ICommandExecutor" />
///     Application developer does not need to use this class directly
/// </summary>
public class CommandExecutor : ICommandExecutor
{
    private static readonly SemaphoreSlim SemaphoreInMemory = new(1, 1);
    private readonly IAggregateLoader _aggregateLoader;
    private readonly ICommandExecuteAwaiter _commandExecuteAwaiter;
    private readonly IDocumentWriter _documentWriter;
    private readonly IServiceProvider _serviceProvider;
    private readonly IUserInformationFactory _userInformationFactory;
    public CommandExecutor(
        IDocumentWriter documentWriter,
        IServiceProvider serviceProvider,
        IAggregateLoader aggregateLoader,
        IUserInformationFactory userInformationFactory,
        ICommandExecuteAwaiter commandExecuteAwaiter)
    {
        _documentWriter = documentWriter;
        _serviceProvider = serviceProvider;
        _aggregateLoader = aggregateLoader;
        _userInformationFactory = userInformationFactory;
        _commandExecuteAwaiter = commandExecuteAwaiter;
    }
    public async Task<CommandExecutorResponse> ExecCommandAsync<TCommand>(TCommand command, List<CallHistory>? callHistories = null)
        where TCommand : ICommandCommon
    {
        if (!command.GetType().IsCommandType()) { throw new SekibanCommandNotRegisteredException(command.GetType().Name); }
        var method = GetType().GetMethod(nameof(ExecCommandAsyncTyped)) ?? throw new Exception("Method not found");
        var genericMethod = method.MakeGenericMethod(command.GetType().GetAggregatePayloadTypeFromCommandType(), command.GetType());
        var (response, generatedEvents)
            = ((CommandExecutorResponse, List<IEvent>))await (dynamic)(genericMethod.Invoke(this, new object?[] { command, callHistories }) ??
                throw new SekibanCommandHandlerNotMatchException("Command failed to execute " + command.GetType().Name));
        return response;
    }

    public async Task<CommandExecutorResponseWithEvents> ExecCommandWithEventsAsync<TCommand>(
        TCommand command,
        List<CallHistory>? callHistories = null) where TCommand : ICommandCommon
    {
        if (!command.GetType().IsCommandType()) { throw new SekibanCommandNotRegisteredException(command.GetType().Name); }
        var method = GetType().GetMethod(nameof(ExecCommandAsyncTyped)) ?? throw new Exception("Method not found");
        var genericMethod = method.MakeGenericMethod(command.GetType().GetAggregatePayloadTypeFromCommandType(), command.GetType());
        var (response, generatedEvents)
            = ((CommandExecutorResponse, List<IEvent>))await (dynamic)(genericMethod.Invoke(this, new object?[] { command, callHistories }) ??
                throw new SekibanCommandHandlerNotMatchException("Command failed to execute " + command.GetType().Name));
        return new CommandExecutorResponseWithEvents(response, generatedEvents.ToImmutableList());
    }


    public async Task<CommandExecutorResponse> ExecCommandWithoutValidationAsync<TCommand>(TCommand command, List<CallHistory>? callHistories = null)
        where TCommand : ICommandCommon
    {
        if (!command.GetType().IsCommandType()) { throw new SekibanCommandNotRegisteredException(command.GetType().Name); }
        var method = GetType().GetMethod(nameof(ExecCommandWithoutValidationAsyncTyped)) ?? throw new Exception("Method not found");
        var genericMethod = method.MakeGenericMethod(command.GetType().GetAggregatePayloadTypeFromCommandType(), command.GetType());
        var (response, generatedEvents)
            = ((CommandExecutorResponse, List<IEvent>))await (dynamic)(genericMethod.Invoke(this, new object?[] { command, callHistories }) ??
                throw new SekibanCommandHandlerNotMatchException("Command failed to execute " + command.GetType().Name));
        return response;
    }

    public async Task<CommandExecutorResponseWithEvents> ExecCommandWithoutValidationWithEventsAsync<TCommand>(
        TCommand command,
        List<CallHistory>? callHistories = null) where TCommand : ICommandCommon
    {
        if (!command.GetType().IsCommandType()) { throw new SekibanCommandNotRegisteredException(command.GetType().Name); }
        var method = GetType().GetMethod(nameof(ExecCommandWithoutValidationAsyncTyped)) ?? throw new Exception("Method not found");
        var genericMethod = method.MakeGenericMethod(command.GetType().GetAggregatePayloadTypeFromCommandType(), command.GetType());
        var (response, generatedEvents)
            = ((CommandExecutorResponse, List<IEvent>))await (dynamic)(genericMethod.Invoke(this, new object?[] { command, callHistories }) ??
                throw new SekibanCommandHandlerNotMatchException("Command failed to execute " + command.GetType().Name));
        return new CommandExecutorResponseWithEvents(response, generatedEvents.ToImmutableList());
    }

    public async Task<(CommandExecutorResponse, List<IEvent>)> ExecCommandAsyncTyped<TAggregatePayload, TCommand>(
        TCommand command,
        List<CallHistory>? callHistories = null) where TAggregatePayload : IAggregatePayloadCommon where TCommand : ICommand<TAggregatePayload>
    {
        var validationResult = command.ValidateProperties()?.ToList();
        if (validationResult?.Any() == true)
        {
            return (new CommandExecutorResponse(
                null,
                null,
                0,
                validationResult,
                null,
                GetAggregatePayloadOut<TAggregatePayload>(new List<IEvent>())), new List<IEvent>());
        }
        return await ExecCommandWithoutValidationAsyncTyped<TAggregatePayload, TCommand>(command, callHistories);
    }

    public async Task<(CommandExecutorResponse, List<IEvent>)> ExecCommandWithoutValidationAsyncTyped<TAggregatePayload, TCommand>(
        TCommand command,
        List<CallHistory>? callHistories = null) where TAggregatePayload : IAggregatePayloadCommon where TCommand : ICommand<TAggregatePayload>
    {
        var rootPartitionKey = command.GetRootPartitionKey();
        var commandDocument
            = new CommandDocument<TCommand>(Guid.Empty, command, typeof(TAggregatePayload), rootPartitionKey, callHistories)
            {
                ExecutedUser = _userInformationFactory.GetCurrentUserInformation()
            };
        List<IEvent> events;
        var commandToSave = command is ICleanupNecessaryCommand<TCommand> cleanupCommand ? cleanupCommand.CleanupCommand(command) : command;
        var version = 0;
        string? lastSortableUniqueId = null;
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(typeof(TAggregatePayload));
        if (aggregateContainerGroup == AggregateContainerGroup.InMemory)
        {
            await SemaphoreInMemory.WaitAsync();
        }
        var aggregateId = command.GetAggregateId();
        try
        {

            var handler
                = _serviceProvider.GetService(typeof(ICommandHandlerCommon<TAggregatePayload, TCommand>)) as
                    ICommandHandlerCommon<TAggregatePayload, TCommand>;
            if (handler is null)
            {
                throw new SekibanCommandNotRegisteredException(typeof(TCommand).Name);
            }

            await _commandExecuteAwaiter.WaitUntilOtherThreadFinished<TAggregatePayload>(aggregateId);
            _commandExecuteAwaiter.StartTask<TAggregatePayload>(aggregateId);
            commandDocument
                = new CommandDocument<TCommand>(aggregateId, command, typeof(TAggregatePayload), rootPartitionKey, callHistories)
                {
                    ExecutedUser = _userInformationFactory.GetCurrentUserInformation()
                };
            if (command is IOnlyPublishingCommandCommon)
            {
                var baseClass = typeof(OnlyPublishingCommandHandlerAdapter<,>);
                var adapterClass = baseClass.MakeGenericType(typeof(TAggregatePayload), typeof(TCommand));
                var adapter = Activator.CreateInstance(adapterClass) ?? throw new Exception("Method not found");
                var method = adapterClass.GetMethod("HandleCommandAsync") ?? throw new Exception("HandleCommandAsync not found");
                var commandResponse
                    = (CommandResponse)await ((dynamic?)method.Invoke(adapter, new object?[] { commandDocument, handler, aggregateId }) ??
                        throw new SekibanCommandHandlerNotMatchException("Command failed to execute " + command.GetType().Name));
                events = await HandleEventsAsync<TAggregatePayload, TCommand>(commandResponse.Events, commandDocument);
                version = commandResponse.Version;
                lastSortableUniqueId = commandResponse.LastSortableUniqueId;
            } else
            {
                var adapter = new CommandHandlerAdapter<TAggregatePayload, TCommand>(_aggregateLoader);
                var commandResponse = await adapter.HandleCommandAsync(commandDocument, handler, aggregateId, rootPartitionKey);
                events = await HandleEventsAsync<TAggregatePayload, TCommand>(commandResponse.Events, commandDocument);
                version = commandResponse.Version;
                lastSortableUniqueId = commandResponse.LastSortableUniqueId;
            }
        }
        catch (Exception e)
        {
            commandDocument = commandDocument with { Exception = SekibanJsonHelper.Serialize(e) };
            throw;
        }
        finally
        {
            _commandExecuteAwaiter.EndTask<TAggregatePayload>(aggregateId);
            await _documentWriter.SaveAsync(commandDocument with { Payload = commandToSave }, typeof(TAggregatePayload));
            if (aggregateContainerGroup == AggregateContainerGroup.InMemory)
            {
                SemaphoreInMemory.Release();
            }
        }

        return (new CommandExecutorResponse(
            commandDocument.AggregateId,
            commandDocument.Id,
            version,
            null,
            lastSortableUniqueId,
            GetAggregatePayloadOut<TAggregatePayload>(events)), events);
    }

    private string GetAggregatePayloadOut<TAggregatePayload>(IEnumerable<IEvent> events)
    {
        var enumerable = events.ToList();
        return enumerable.Any() ? enumerable.Last().GetPayload().GetAggregatePayloadOutType().Name : typeof(TAggregatePayload).Name;
    }

    private async Task<List<IEvent>> HandleEventsAsync<TAggregatePayload, TCommand>(
        IReadOnlyCollection<IEvent> events,
        CommandDocument<TCommand> commandDocument) where TAggregatePayload : IAggregatePayloadCommon where TCommand : ICommand<TAggregatePayload>
    {
        var toReturnEvents = new List<IEvent>();
        if (events.Any())
        {
            foreach (var ev in events)
            {
                ev.CallHistories.AddRange(commandDocument.GetCallHistoriesIncludesItself());
            }
            toReturnEvents.AddRange(events);
            foreach (var ev in events)
            {
                await _documentWriter.SaveAndPublishEvent(ev, typeof(TAggregatePayload));
            }
        }

        return toReturnEvents;
    }
}
