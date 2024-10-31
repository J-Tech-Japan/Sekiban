using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command.UserInformation;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.History;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot.Aggregate;
using Sekiban.Core.Snapshot.Aggregate.Commands;
using Sekiban.Core.Types;
using Sekiban.Core.Usecase;
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
    public async Task<CommandExecutorResponse> ExecCommandAsync<TCommand>(
        TCommand command,
        List<CallHistory>? callHistories = null) where TCommand : ICommandCommon
    {
        var withResult = await ExecCommandWithResultAsync(command, callHistories);
        return withResult.UnwrapBox();
    }
    public async Task<ResultBox<CommandExecutorResponse>> ExecCommandWithResultAsync<TCommand>(
        TCommand command,
        List<CallHistory>? callHistories = null) where TCommand : ICommandCommon
    {
        try
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
                        return await ExecCommandAsync(
                            convertedCommand,
                            CallHistoriesWithConverter(converter, callHistories));
                    }
                }
            }

            if (!command.GetType().IsCommandType())
            {
                throw new SekibanCommandNotRegisteredException(command.GetType().Name);
            }
            var method = GetType().GetMethod(nameof(ExecCommandAsyncTyped)) ??
                throw new MissingMethodException("Method not found");
            var genericMethod = method.MakeGenericMethod(
                command.GetType().GetAggregatePayloadTypeFromCommandType(),
                command.GetType());

            var resultbox
                = (ResultBox<TwoValues<CommandExecutorResponse, List<IEvent>>>)await (dynamic)
                    (genericMethod.Invoke(this, [command, callHistories]) ??
                        throw new SekibanCommandHandlerNotMatchException(
                            "Command failed to execute " + command.GetType().Name));
            return resultbox.Remap((response, _) => response);
        }
        catch (Exception e)
        {
            return e;
        }
    }

    public async Task<CommandExecutorResponseWithEvents> ExecCommandWithEventsAsync<TCommand>(
        TCommand command,
        List<CallHistory>? callHistories = null) where TCommand : ICommandCommon
    {
        var result = await ExecCommandWithEventsWithResultAsync(command, callHistories);
        return result.UnwrapBox();
    }
    public async Task<ResultBox<CommandExecutorResponseWithEvents>> ExecCommandWithEventsWithResultAsync<TCommand>(
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
                    return await ExecCommandWithEventsAsync(
                        convertedCommand,
                        CallHistoriesWithConverter(converter, callHistories));
                }
            }
        }

        if (!command.GetType().IsCommandType())
        {
            throw new SekibanCommandNotRegisteredException(command.GetType().Name);
        }
        var method = GetType().GetMethod(nameof(ExecCommandAsyncTyped)) ??
            throw new MissingMethodException("Method not found");
        var genericMethod = method.MakeGenericMethod(
            command.GetType().GetAggregatePayloadTypeFromCommandType(),
            command.GetType());
        var resultBox
            = (ResultBox<TwoValues<CommandExecutorResponse, List<IEvent>>>)await (dynamic)
                (genericMethod.Invoke(this, [command, callHistories]) ??
                    throw new SekibanCommandHandlerNotMatchException(
                        "Command failed to execute " + command.GetType().Name));
        return resultBox.Remap(
            values => new CommandExecutorResponseWithEvents(values.Value1, values.Value2.ToImmutableList()));
    }


    public async Task<CommandExecutorResponse> ExecCommandWithoutValidationAsync<TCommand>(
        TCommand command,
        List<CallHistory>? callHistories = null) where TCommand : ICommandCommon =>
        await ExecCommandWithoutValidationWithResultAsync(command, callHistories).UnwrapBox();
    public async Task<ResultBox<CommandExecutorResponse>> ExecCommandWithoutValidationWithResultAsync<TCommand>(
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
                    return await ExecCommandWithoutValidationAsync(
                        convertedCommand,
                        CallHistoriesWithConverter(converter, callHistories));
                }
            }
        }

        if (!command.GetType().IsCommandType())
        {
            throw new SekibanCommandNotRegisteredException(command.GetType().Name);
        }
        var method = GetType().GetMethod(nameof(ExecCommandWithoutValidationAsyncTyped)) ??
            throw new MissingMethodException("Method not found");
        var genericMethod = method.MakeGenericMethod(
            command.GetType().GetAggregatePayloadTypeFromCommandType(),
            command.GetType());
        var resultBox
            = (ResultBox<TwoValues<CommandExecutorResponse, List<IEvent>>>)await (dynamic)
                (genericMethod.Invoke(this, [command, callHistories]) ??
                    throw new SekibanCommandHandlerNotMatchException(
                        "Command failed to execute " + command.GetType().Name));
        return resultBox.Remap((response, _) => response);
    }

    public async Task<CommandExecutorResponseWithEvents> ExecCommandWithoutValidationWithEventsAsync<TCommand>(
        TCommand command,
        List<CallHistory>? callHistories = null) where TCommand : ICommandCommon =>
        await ExecCommandWithoutValidationWithEventsWithResultAsync(command, callHistories).UnwrapBox();
    public async Task<ResultBox<CommandExecutorResponseWithEvents>>
        ExecCommandWithoutValidationWithEventsWithResultAsync<TCommand>(
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
                    return await ExecCommandWithoutValidationWithEventsAsync(
                        convertedCommand,
                        CallHistoriesWithConverter(converter, callHistories));
                }
            }
        }
        if (!command.GetType().IsCommandType())
        {
            throw new SekibanCommandNotRegisteredException(command.GetType().Name);
        }
        var method = GetType().GetMethod(nameof(ExecCommandWithoutValidationAsyncTyped)) ??
            throw new MissingMethodException("Method not found");
        var genericMethod = method.MakeGenericMethod(
            command.GetType().GetAggregatePayloadTypeFromCommandType(),
            command.GetType());
        var resultBox
            = (ResultBox<TwoValues<CommandExecutorResponse, List<IEvent>>>)await (dynamic)
                (genericMethod.Invoke(this, [command, callHistories]) ??
                    throw new SekibanCommandHandlerNotMatchException(
                        "Command failed to execute " + command.GetType().Name));
        return resultBox.Remap(
            values => new CommandExecutorResponseWithEvents(values.Value1, values.Value2.ToImmutableList()));
    }

    private List<CallHistory> CallHistoriesWithConverter(
        ICommandConverterCommon converter,
        List<CallHistory>? callHistories)
    {
        var toAdd = new CallHistory(
            Guid.Empty,
            converter.GetType().Name,
            userInformationFactory.GetCurrentUserInformation());
        return [..callHistories ?? new List<CallHistory>(), toAdd];
    }

    public async Task<ResultBox<TwoValues<CommandExecutorResponse, List<IEvent>>>>
        ExecCommandAsyncTyped<TAggregatePayload, TCommand>(TCommand command, List<CallHistory>? callHistories = null)
        where TAggregatePayload : IAggregatePayloadGeneratable<TAggregatePayload>
        where TCommand : ICommandCommon<TAggregatePayload>
    {
        var validationResult = command.ValidateProperties().ToList();
        if (validationResult.Count != 0)
        {
            return TwoValues.FromValues(
                new CommandExecutorResponse(
                    null,
                    null,
                    0,
                    validationResult,
                    null,
                    GetAggregatePayloadOut<TAggregatePayload>(Enumerable.Empty<IEvent>()),
                    0),
                Enumerable.Empty<IEvent>().ToList());
        }

        return await ExecCommandWithoutValidationAsyncTyped<TAggregatePayload, TCommand>(command, callHistories);
    }

    public async Task<ResultBox<TwoValues<CommandExecutorResponse, List<IEvent>>>>
        ExecCommandWithoutValidationAsyncTyped<TAggregatePayload, TCommand>(
            TCommand command,
            List<CallHistory>? callHistories = null)
        where TAggregatePayload : IAggregatePayloadGeneratable<TAggregatePayload>
        where TCommand : ICommandCommon<TAggregatePayload>
    {
        var rootPartitionKey = command.GetRootPartitionKey();
        if (!CommandRootPartitionValidationAttribute.IsValidRootPartitionKey(rootPartitionKey))
        {
            return ResultBox<TwoValues<CommandExecutorResponse, List<IEvent>>>.FromException(
                new SekibanInvalidRootPartitionKeyException(rootPartitionKey));
        }
        var commandDocument = new CommandDocument<TCommand>(
            Guid.Empty,
            command,
            typeof(TAggregatePayload),
            rootPartitionKey,
            callHistories)
        {
            ExecutedUser = userInformationFactory.GetCurrentUserInformation()
        };
        var events = new List<IEvent>();
        var commandToSave = command is ICleanupNecessaryCommand<TCommand> cleanupCommand
            ? cleanupCommand.CleanupCommand(command)
            : command;
        var version = 0;
        string? lastSortableUniqueId = null;
        var aggregateContainerGroup
            = AggregateContainerGroupAttribute.FindAggregateContainerGroup(typeof(TAggregatePayload));
        if (aggregateContainerGroup == AggregateContainerGroup.InMemory)
        {
            await SemaphoreInMemory.WaitAsync();
        }
        var aggregateId = GetAggregateId<TAggregatePayload>(command, serviceProvider);
        try
        {

            if (command is not ICommandWithoutLoadingAggregateCommon)
            {
                await SemaphoreAwaiter.WaitAsync();
                await commandExecuteAwaiter.WaitUntilOtherThreadFinished<TAggregatePayload>(aggregateId);
                await commandExecuteAwaiter.StartTaskAsync<TAggregatePayload>(aggregateId);
                SemaphoreAwaiter.Release();
            }
            commandDocument = new CommandDocument<TCommand>(
                aggregateId,
                command,
                typeof(TAggregatePayload),
                rootPartitionKey,
                callHistories)
            {
                ExecutedUser = userInformationFactory.GetCurrentUserInformation()
            };
            switch (command)
            {
                case ICommandWithoutLoadingAggregateCommon
                    when command is not ICommandWithHandlerCommon<TAggregatePayload, TCommand>:
                {
                    var handler
                        = serviceProvider.GetService(typeof(ICommandHandlerCommon<TAggregatePayload, TCommand>)) as
                            ICommandHandlerCommon<TAggregatePayload, TCommand> ??
                        throw new SekibanCommandNotRegisteredException(typeof(TCommand).Name);
                    var baseClass = typeof(CommandWithoutLoadingAggregateHandlerAdapter<,>);
                    var adapterClass = baseClass.MakeGenericType(typeof(TAggregatePayload), typeof(TCommand));
                    var adapter = Activator.CreateInstance(adapterClass) ??
                        throw new MissingMethodException("Method not found");
                    var method = adapterClass.GetMethod(
                            nameof(CommandWithoutLoadingAggregateHandlerAdapter<SnapshotManager,
                                ICommandWithoutLoadingAggregate<SnapshotManager>>.HandleCommandAsync)) ??
                        throw new MissingMethodException("HandleCommandAsync not found");
                    var commandResponse
                        = (CommandResponse)await ((dynamic?)method.Invoke(
                                adapter,
                                [commandDocument, handler, aggregateId, rootPartitionKey]) ??
                            throw new SekibanCommandHandlerNotMatchException(
                                "Command failed to execute " + command.GetType().Name));
                    events = await HandleEventsAsync<TAggregatePayload, TCommand>(
                        commandResponse.Events,
                        commandDocument);
                    version = commandResponse.Version;
                    lastSortableUniqueId = commandResponse.LastSortableUniqueId;
                    break;
                }
                case ICommandWithoutLoadingAggregateCommon and ICommandWithHandlerCommon<TAggregatePayload, TCommand>:
                {
                    var baseClass = typeof(StaticCommandWithoutLoadingAggregateHandlerAdapter<,>);
                    var adapterClass = baseClass.MakeGenericType(typeof(TAggregatePayload), typeof(TCommand));
                    var adapter = Activator.CreateInstance(adapterClass, serviceProvider) ??
                        throw new MissingMethodException("Method not found");
                    var method = adapterClass.GetMethod(nameof(ICommandHandlerAdapterCommon.HandleCommandAsync)) ??
                        throw new MissingMethodException("HandleCommandAsync not found");
                    var commandResponse
                        = (ResultBox<CommandResponse>)await ((dynamic?)method.Invoke(
                                adapter,
                                [commandDocument, aggregateId, rootPartitionKey]) ??
                            throw new SekibanCommandHandlerNotMatchException(
                                "Command failed to execute " + command.GetType().Name));
                    switch (commandResponse)
                    {
                        case { IsSuccess: true }:
                            events = await HandleEventsAsync<TAggregatePayload, TCommand>(
                                commandResponse.GetValue().Events,
                                commandDocument);
                            version = commandResponse.GetValue().Version;
                            lastSortableUniqueId = commandResponse.GetValue().LastSortableUniqueId;
                            break;
                        case { IsSuccess: false }:
                            commandDocument = commandDocument with
                            {
                                Exception = SekibanJsonHelper.Serialize(commandResponse.GetException())
                            };
                            break;
                    }
                    var document = commandDocument;
                    return commandResponse.Remap(
                        _ => TwoValues.FromValues(
                            new CommandExecutorResponse(
                                document.AggregateId,
                                document.Id,
                                version,
                                null,
                                lastSortableUniqueId,
                                GetAggregatePayloadOut<TAggregatePayload>(events),
                                events.Count),
                            events));
                }
                case ICommandWithHandlerCommon<TAggregatePayload, TCommand>:
                {
                    var parent = typeof(TAggregatePayload).GetBaseAggregatePayloadTypeFromAggregate();
                    var baseClass = typeof(StaticCommandHandlerAdapter<,,>);
                    var adapterClass = baseClass.MakeGenericType(parent, typeof(TAggregatePayload), typeof(TCommand));
                    var adapter = Activator.CreateInstance(adapterClass, aggregateLoader, serviceProvider, true) ??
                        throw new MissingMethodException("Method not found");
                    var method = adapterClass.GetMethod(nameof(ICommandHandlerAdapterCommon.HandleCommandAsync)) ??
                        throw new MissingMethodException("HandleCommandAsync not found");
                    var commandResponse
                        = (ResultBox<CommandResponse>)await ((dynamic?)method.Invoke(
                                adapter,
                                [commandDocument, aggregateId, rootPartitionKey]) ??
                            throw new SekibanCommandHandlerNotMatchException(
                                "Command failed to execute " + command.GetType().Name));
                    switch (commandResponse)
                    {
                        case { IsSuccess: true }:
                            events = await HandleEventsAsync<TAggregatePayload, TCommand>(
                                commandResponse.GetValue().Events,
                                commandDocument);
                            version = commandResponse.GetValue().Version;
                            lastSortableUniqueId = commandResponse.GetValue().LastSortableUniqueId;
                            break;
                        case { IsSuccess: false }:
                            commandDocument = commandDocument with
                            {
                                Exception = SekibanJsonHelper.Serialize(commandResponse.GetException())
                            };
                            break;
                    }
                    var document = commandDocument;
                    return commandResponse.Remap(
                        _ => TwoValues.FromValues(
                            new CommandExecutorResponse(
                                document.AggregateId,
                                document.Id,
                                version,
                                null,
                                lastSortableUniqueId,
                                GetAggregatePayloadOut<TAggregatePayload>(events),
                                events.Count),
                            events));
                }
                default:
                {
                    var handler
                        = serviceProvider.GetService(typeof(ICommandHandlerCommon<TAggregatePayload, TCommand>)) as
                            ICommandHandlerCommon<TAggregatePayload, TCommand> ??
                        throw new SekibanCommandNotRegisteredException(typeof(TCommand).Name);
                    var adapter = new CommandHandlerAdapter<TAggregatePayload, TCommand>(
                        aggregateLoader,
                        serviceProvider);
                    var commandResponse = await adapter.HandleCommandAsync(
                        commandDocument,
                        handler,
                        aggregateId,
                        rootPartitionKey);
                    events = await HandleEventsAsync<TAggregatePayload, TCommand>(
                        commandResponse.Events,
                        commandDocument);
                    version = commandResponse.Version;
                    lastSortableUniqueId = commandResponse.LastSortableUniqueId;
                    break;
                }
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
            await documentWriter.SaveAsync(
                commandDocument with { Payload = commandToSave },
                new AggregateWriteStream(typeof(TAggregatePayload)));
            if (aggregateContainerGroup == AggregateContainerGroup.InMemory)
            {
                SemaphoreInMemory.Release();
            }
        }

        return TwoValues.FromValues(
            new CommandExecutorResponse(
                commandDocument.AggregateId,
                commandDocument.Id,
                version,
                null,
                lastSortableUniqueId,
                GetAggregatePayloadOut<TAggregatePayload>(events),
                events.Count),
            events);
    }

    public static Guid GetAggregateId<TAggregatePayload>(ICommandCommon command, IServiceProvider serviceProvider)
        where TAggregatePayload : IAggregatePayloadCommon => command switch
    {
        _ when command.GetType().IsCommandWithHandlerType() => GetAggregateIdFromCommand(command),
        _ => GetAggregateIdFromHandler<TAggregatePayload, ICommandCommon>(command, serviceProvider)
    };
    public static Guid GetAggregateIdFromHandler<TAggregatePayload, TCommand>(
        TCommand command,
        IServiceProvider serviceProvider) where TAggregatePayload : IAggregatePayloadCommon
        where TCommand : ICommandCommon
    {
        var baseClass = typeof(ICommandHandlerCommon<,>);
        var genericClass = baseClass.MakeGenericType(typeof(TAggregatePayload), command.GetType());

        var handler = serviceProvider.GetService(genericClass) ??
            throw new SekibanCommandNotRegisteredException(typeof(TCommand).Name);

        var method = handler
                .GetType()
                .GetMethod(nameof(ICommandHandlerCommon<SnapshotManager, CreateSnapshotManager>.SpecifyAggregateId)) ??
            throw new MissingMethodException("Method not found");

        return method.Invoke(handler, [command]) as Guid? ??
            throw new SekibanInvalidArgumentException("AggregateId is null");
    }
    public static Guid GetAggregateIdFromCommand<TCommand>(TCommand command) where TCommand : notnull
    {
        if (!command.GetType().IsCommandWithHandlerType())
        {
            throw new SekibanCommandNotRegisteredException(
                $"Command {command.GetType().Name} needs to inherit ICommandWithHandler");
        }
        var commandClass = command.GetType();
        var method = commandClass.GetMethod(
                nameof(ICommandWithHandler<SnapshotManager, CreateSnapshotManager>.SpecifyAggregateId)) ??
            throw new MissingMethodException("Method not found");
        return (Guid?)method.Invoke(commandClass, [command]) ??
            throw new SekibanInvalidArgumentException("AggregateId is null");
    }
    private static string GetAggregatePayloadOut<TAggregatePayload>(IEnumerable<IEvent> events)
    {
        var list = events.ToList();
        return list.Count != 0
            ? list[^1].GetPayload().GetAggregatePayloadOutType().Name
            : typeof(TAggregatePayload).Name;
    }

    private async Task<List<IEvent>> HandleEventsAsync<TAggregatePayload, TCommand>(
        IReadOnlyCollection<IEvent> events,
        CommandDocument<TCommand> commandDocument) where TAggregatePayload : IAggregatePayloadCommon
        where TCommand : ICommandCommon<TAggregatePayload>
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
        await documentWriter.SaveAndPublishEvents(events, new AggregateWriteStream(typeof(TAggregatePayload)));
        return toReturnEvents;
    }


    public static Func<TCommon, ISekibanExecutor, Task<IResult>> CreateSimpleCommandExecutor<TCommon>()
        where TCommon : class, ICommandCommon =>
        ([FromBody] input, [FromServices] executor) => executor.ExecuteCommand(input).ToResults();
    public static Func<TCommon, ISekibanExecutor, Task<IResult>> CreateSimpleCommandExecutorWithErrorHandler<TCommon>(
        Func<Exception, IResult> errorHandler) where TCommon : class, ICommandCommon =>
        ([FromBody] input, [FromServices] executor) => executor.ExecuteCommand(input).ToResults();
}
