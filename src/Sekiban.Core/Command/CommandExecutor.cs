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
public class CommandExecutor(
    IDocumentWriter documentWriter,
    IServiceProvider serviceProvider,
    IAggregateLoader aggregateLoader,
    IUserInformationFactory userInformationFactory,
    ICommandExecuteAwaiter commandExecuteAwaiter) : ICommandExecutor
{
    private static readonly SemaphoreSlim SemaphoreInMemory = new(1, 1);
    private static readonly SemaphoreSlim SemaphoreAwaiter = new(1, 1);
    public async Task<CommandExecutorResponse> ExecCommandAsync<TCommand>(TCommand command, List<CallHistory>? callHistories = null)
        where TCommand : ICommandCommon
    {


        if (command is ICommandConverterCommon converter)
        {
            var validationResult = command.ValidateProperties().ToList();
            if (validationResult.Count != 0)
            {
                return new CommandExecutorResponse(
                    null,
                    null,
                    0,
                    validationResult,
                    null,
                    converter.GetType().GetAggregatePayloadTypeFromCommandType().Name,
                    0);
            }
            var commandHandlerCommonType = typeof(ICommandHandlerCommon<,>).MakeGenericType(
                converter.GetType().GetAggregatePayloadTypeFromCommandType(),
                converter.GetType());
            if (serviceProvider.GetService(commandHandlerCommonType) is ICommandConverterHandlerCommon handler)
            {
                if (((dynamic)handler).ConvertCommand((dynamic)converter) is ICommandCommon convertedCommand)
                {
                    return await ExecCommandAsync(convertedCommand, CallHistoriesWithConverter(converter, callHistories));
                }
            }
        }

        if (!command.GetType().IsCommandType()) { throw new SekibanCommandNotRegisteredException(command.GetType().Name); }
        var method = GetType().GetMethod(nameof(ExecCommandAsyncTyped)) ?? throw new MissingMethodException("Method not found");
        var genericMethod = method.MakeGenericMethod(command.GetType().GetAggregatePayloadTypeFromCommandType(), command.GetType());

        var (response, _) = ((CommandExecutorResponse, List<IEvent>))await (dynamic)(genericMethod.Invoke(this, [command, callHistories]) ??
            throw new SekibanCommandHandlerNotMatchException("Command failed to execute " + command.GetType().Name));
        return response;
    }

    public async Task<CommandExecutorResponseWithEvents> ExecCommandWithEventsAsync<TCommand>(
        TCommand command,
        List<CallHistory>? callHistories = null) where TCommand : ICommandCommon
    {
        if (command is ICommandConverterCommon converter)
        {
            var validationResult = command.ValidateProperties().ToList();
            if (validationResult.Count != 0)
            {
                return new CommandExecutorResponseWithEvents(
                    new CommandExecutorResponse(
                        null,
                        null,
                        0,
                        validationResult,
                        null,
                        converter.GetType().GetAggregatePayloadTypeFromCommandType().Name,
                        0),
                    Enumerable.Empty<IEvent>().ToImmutableList());
            }
            var commandHandlerCommonType = typeof(ICommandHandlerCommon<,>).MakeGenericType(
                converter.GetType().GetAggregatePayloadTypeFromCommandType(),
                converter.GetType());
            if (serviceProvider.GetService(commandHandlerCommonType) is ICommandConverterHandlerCommon handler)
            {
                if (((dynamic)handler).ConvertCommand((dynamic)converter) is ICommandCommon convertedCommand)
                {
                    return await ExecCommandWithEventsAsync(convertedCommand, CallHistoriesWithConverter(converter, callHistories));
                }
            }
        }

        if (!command.GetType().IsCommandType()) { throw new SekibanCommandNotRegisteredException(command.GetType().Name); }
        var method = GetType().GetMethod(nameof(ExecCommandAsyncTyped)) ?? throw new MissingMethodException("Method not found");
        var genericMethod = method.MakeGenericMethod(command.GetType().GetAggregatePayloadTypeFromCommandType(), command.GetType());
        var (response, generatedEvents)
            = ((CommandExecutorResponse, List<IEvent>))await (dynamic)(genericMethod.Invoke(this, [command, callHistories]) ??
                throw new SekibanCommandHandlerNotMatchException("Command failed to execute " + command.GetType().Name));
        return new CommandExecutorResponseWithEvents(response, generatedEvents.ToImmutableList());
    }


    public async Task<CommandExecutorResponse> ExecCommandWithoutValidationAsync<TCommand>(TCommand command, List<CallHistory>? callHistories = null)
        where TCommand : ICommandCommon
    {
        if (command is ICommandConverterCommon converter)
        {
            var commandHandlerCommonType = typeof(ICommandHandlerCommon<,>).MakeGenericType(
                converter.GetType().GetAggregatePayloadTypeFromCommandType(),
                converter.GetType());
            if (serviceProvider.GetService(commandHandlerCommonType) is ICommandConverterHandlerCommon handler)
            {
                if (((dynamic)handler).ConvertCommand((dynamic)converter) is ICommandCommon convertedCommand)
                {
                    return await ExecCommandWithoutValidationAsync(convertedCommand, CallHistoriesWithConverter(converter, callHistories));
                }
            }
        }

        if (!command.GetType().IsCommandType()) { throw new SekibanCommandNotRegisteredException(command.GetType().Name); }
        var method = GetType().GetMethod(nameof(ExecCommandWithoutValidationAsyncTyped)) ?? throw new MissingMethodException("Method not found");
        var genericMethod = method.MakeGenericMethod(command.GetType().GetAggregatePayloadTypeFromCommandType(), command.GetType());
        var (response, _) = ((CommandExecutorResponse, List<IEvent>))await (dynamic)(genericMethod.Invoke(this, [command, callHistories]) ??
            throw new SekibanCommandHandlerNotMatchException("Command failed to execute " + command.GetType().Name));
        return response;
    }

    public async Task<CommandExecutorResponseWithEvents> ExecCommandWithoutValidationWithEventsAsync<TCommand>(
        TCommand command,
        List<CallHistory>? callHistories = null) where TCommand : ICommandCommon
    {
        if (command is ICommandConverterCommon converter)
        {
            var commandHandlerCommonType = typeof(ICommandHandlerCommon<,>).MakeGenericType(
                converter.GetType().GetAggregatePayloadTypeFromCommandType(),
                converter.GetType());
            if (serviceProvider.GetService(commandHandlerCommonType) is ICommandConverterHandlerCommon handler)
            {
                if (((dynamic)handler).ConvertCommand((dynamic)converter) is ICommandCommon convertedCommand)
                {
                    return await ExecCommandWithoutValidationWithEventsAsync(convertedCommand, CallHistoriesWithConverter(converter, callHistories));
                }
            }
        }
        if (!command.GetType().IsCommandType()) { throw new SekibanCommandNotRegisteredException(command.GetType().Name); }
        var method = GetType().GetMethod(nameof(ExecCommandWithoutValidationAsyncTyped)) ?? throw new MissingMethodException("Method not found");
        var genericMethod = method.MakeGenericMethod(command.GetType().GetAggregatePayloadTypeFromCommandType(), command.GetType());
        var (response, generatedEvents)
            = ((CommandExecutorResponse, List<IEvent>))await (dynamic)(genericMethod.Invoke(this, [command, callHistories]) ??
                throw new SekibanCommandHandlerNotMatchException("Command failed to execute " + command.GetType().Name));
        return new CommandExecutorResponseWithEvents(response, generatedEvents.ToImmutableList());
    }

    private List<CallHistory> CallHistoriesWithConverter(ICommandConverterCommon converter, List<CallHistory>? callHistories)
    {
        var toAdd = new CallHistory(Guid.Empty, converter.GetType().Name, userInformationFactory.GetCurrentUserInformation());
        return [..callHistories ?? new List<CallHistory>(), toAdd];
    }

    public async Task<(CommandExecutorResponse, List<IEvent>)> ExecCommandAsyncTyped<TAggregatePayload, TCommand>(
        TCommand command,
        List<CallHistory>? callHistories = null) where TAggregatePayload : IAggregatePayloadGeneratable<TAggregatePayload>
        where TCommand : ICommand<TAggregatePayload>
    {
        var validationResult = command.ValidateProperties().ToList();
        if (validationResult.Count != 0)
        {
            return (new CommandExecutorResponse(
                null,
                null,
                0,
                validationResult,
                null,
                GetAggregatePayloadOut<TAggregatePayload>(Enumerable.Empty<IEvent>()),
                0), Enumerable.Empty<IEvent>().ToList());
        }

        return await ExecCommandWithoutValidationAsyncTyped<TAggregatePayload, TCommand>(command, callHistories);
    }

    public async Task<(CommandExecutorResponse, List<IEvent>)> ExecCommandWithoutValidationAsyncTyped<TAggregatePayload, TCommand>(
        TCommand command,
        List<CallHistory>? callHistories = null) where TAggregatePayload : IAggregatePayloadGeneratable<TAggregatePayload>
        where TCommand : ICommand<TAggregatePayload>
    {
        var rootPartitionKey = command.GetRootPartitionKey();
        if (!CommandRootPartitionValidationAttribute.IsValidRootPartitionKey(rootPartitionKey))
        {
            throw new SekibanInvalidRootPartitionKeyException(rootPartitionKey);
        }
        var commandDocument
            = new CommandDocument<TCommand>(Guid.Empty, command, typeof(TAggregatePayload), rootPartitionKey, callHistories)
            {
                ExecutedUser = userInformationFactory.GetCurrentUserInformation()
            };
        List<IEvent> events;
        var commandToSave = command is ICleanupNecessaryCommand<TCommand> cleanupCommand ? cleanupCommand.CleanupCommand(command) : command;
        int version;
        string? lastSortableUniqueId;
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(typeof(TAggregatePayload));
        if (aggregateContainerGroup == AggregateContainerGroup.InMemory)
        {
            await SemaphoreInMemory.WaitAsync();
        }
        var aggregateId = command.GetAggregateId();
        try
        {

            var handler
                = serviceProvider.GetService(typeof(ICommandHandlerCommon<TAggregatePayload, TCommand>)) as
                    ICommandHandlerCommon<TAggregatePayload, TCommand> ??
                throw new SekibanCommandNotRegisteredException(typeof(TCommand).Name);
            if (command is not ICommandWithoutLoadingAggregateCommon)
            {
                await SemaphoreAwaiter.WaitAsync();
                await commandExecuteAwaiter.WaitUntilOtherThreadFinished<TAggregatePayload>(aggregateId);
                await commandExecuteAwaiter.StartTaskAsync<TAggregatePayload>(aggregateId);
                SemaphoreAwaiter.Release();
            }
            commandDocument
                = new CommandDocument<TCommand>(aggregateId, command, typeof(TAggregatePayload), rootPartitionKey, callHistories)
                {
                    ExecutedUser = userInformationFactory.GetCurrentUserInformation()
                };
            if (command is ICommandWithoutLoadingAggregateCommon)
            {
                var baseClass = typeof(CommandWithoutLoadingAggregateHandlerAdapter<,>);
                var adapterClass = baseClass.MakeGenericType(typeof(TAggregatePayload), typeof(TCommand));
                var adapter = Activator.CreateInstance(adapterClass) ?? throw new MissingMethodException("Method not found");
                var method = adapterClass.GetMethod("HandleCommandAsync") ?? throw new MissingMethodException("HandleCommandAsync not found");
                var commandResponse
                    = (CommandResponse)await ((dynamic?)method.Invoke(adapter, [commandDocument, handler, aggregateId, rootPartitionKey]) ??
                        throw new SekibanCommandHandlerNotMatchException("Command failed to execute " + command.GetType().Name));
                events = await HandleEventsAsync<TAggregatePayload, TCommand>(commandResponse.Events, commandDocument);
                version = commandResponse.Version;
                lastSortableUniqueId = commandResponse.LastSortableUniqueId;
            } else
            {
                var adapter = new CommandHandlerAdapter<TAggregatePayload, TCommand>(aggregateLoader);
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
            await commandExecuteAwaiter.EndTaskAsync<TAggregatePayload>(aggregateId);
            await documentWriter.SaveAsync(commandDocument with { Payload = commandToSave }, typeof(TAggregatePayload));
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
            GetAggregatePayloadOut<TAggregatePayload>(events),
            events.Count), events);
    }

    private static string GetAggregatePayloadOut<TAggregatePayload>(IEnumerable<IEvent> events)
    {
        var list = events.ToList();
        return list.Count != 0 ? list[^1].GetPayload().GetAggregatePayloadOutType().Name : typeof(TAggregatePayload).Name;
    }

    private async Task<List<IEvent>> HandleEventsAsync<TAggregatePayload, TCommand>(
        IReadOnlyCollection<IEvent> events,
        CommandDocument<TCommand> commandDocument) where TAggregatePayload : IAggregatePayloadCommon where TCommand : ICommand<TAggregatePayload>
    {
        var toReturnEvents = new List<IEvent>();
        if (events.Count == 0)
        {
            return toReturnEvents;
        }
        foreach (var ev in events)
        {
            ev.CallHistories.AddRange(commandDocument.GetCallHistoriesIncludesItself());
        }
        toReturnEvents.AddRange(events);
        await documentWriter.SaveAndPublishEvents(events, typeof(TAggregatePayload));
        return toReturnEvents;
    }
}
