using Sekiban.Core.Aggregate;
using Sekiban.Core.Command.UserInformation;
using Sekiban.Core.Document;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using Sekiban.Core.History;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Shared;
using Sekiban.Core.Validation;
namespace Sekiban.Core.Command;

public class CommandExecutor : ICommandExecutor
{
    private static readonly SemaphoreSlim _semaphoreInMemory = new(1, 1);
    private readonly IDocumentWriter _documentWriter;
    private readonly IServiceProvider _serviceProvider;
    private readonly IUserInformationFactory _userInformationFactory;
    private readonly IAggregateLoader aggregateLoader;

    public CommandExecutor(
        IDocumentWriter documentWriter,
        IServiceProvider serviceProvider,
        IAggregateLoader aggregateLoader,
        IUserInformationFactory userInformationFactory)
    {
        _documentWriter = documentWriter;
        _serviceProvider = serviceProvider;
        this.aggregateLoader = aggregateLoader;
        _userInformationFactory = userInformationFactory;
    }
    // public async Task<(CommandExecutorResponse, List<IEvent>)> ExecChangeCommandAsync<TAggregatePayload, C>(
    //     C command,
    //     List<CallHistory>? callHistories = null) where TAggregatePayload : IAggregatePayload, new()
    //     where C : IVersionValidationCommandBaseTAggregatePayload>
    // {
    //     var validationResult = command.ValidateProperties()?.ToList();
    //     if (validationResult?.Any() == true)
    //     {
    //         return (new CommandExecutorResponse(null, null, 0, validationResult), new List<IEvent>());
    //     }
    //     return await ExecChangeCommandWithoutValidationAsync<TAggregatePayload, C>(command, callHistories);
    // }
    // public async Task<(CommandExecutorResponse, List<IEvent>)> ExecChangeCommandWithoutValidationAsync<TAggregatePayload, C>(
    //     C command,
    //     List<CallHistory>? callHistories = null) where TAggregatePayload : IAggregatePayload, new()
    //     where C : IVersionValidationCommandBaseTAggregatePayload>
    // {
    //     var commandDocument = new CommandDocument<C>(command.GetAggregateId(), command, typeof(TAggregatePayload), callHistories)
    //     {
    //         ExecutedUser = _userInformationFactory.GetCurrentUserInformation()
    //     };
    //
    //     List<IEvent> events;
    //     var version = 0;
    //     var commandToSave = command;
    //     var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(typeof(TAggregatePayload));
    //     if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
    //     {
    //         await _semaphoreInMemory.WaitAsync();
    //     }
    //     try
    //     {
    //         var handler =
    //             _serviceProvider.GetService(typeof(IChangeCommandHandler<TAggregatePayload, C>)) as
    //                 IChangeCommandHandler<TAggregatePayload, C>;
    //         if (handler is null)
    //         {
    //             throw new SekibanCommandNotRegisteredException(typeof(C).Name);
    //         }
    //         if (command is not IOnlyPublishingCommand)
    //         {
    //             var aggregate = await aggregateLoader.AsAggregateAsync<TAggregatePayload>(command.GetAggregateId());
    //             if (aggregate is null)
    //             {
    //                 throw new SekibanInvalidArgumentException();
    //             }
    //             commandToSave = handler.CleanupCommandIfNeeded(commandToSave);
    //             var result = await handler.HandleAsync(commandDocument, aggregate);
    //             version = result.Version;
    //             commandDocument = commandDocument with { AggregateId = result.AggregateId };
    //
    //             events = await HandleEventsAsync<TAggregatePayload, C>(result.Events, commandDocument);
    //             if (result is null)
    //             {
    //                 throw new SekibanInvalidArgumentException();
    //             }
    //         }
    //         else
    //         {
    //             commandToSave = handler.CleanupCommandIfNeeded(commandToSave);
    //             var result = await handler.HandleForOnlyPublishingCommandAsync(commandDocument, command.GetAggregateId());
    //             version = result.Version;
    //             commandDocument = commandDocument with { AggregateId = result.AggregateId };
    //
    //             events = await HandleEventsAsync<TAggregatePayload, C>(result.Events, commandDocument);
    //             if (result is null)
    //             {
    //                 throw new SekibanInvalidArgumentException();
    //             }
    //         }
    //     }
    //     catch (Exception e)
    //     {
    //         commandDocument = commandDocument with { Exception = SekibanJsonHelper.Serialize(e) };
    //         throw;
    //     }
    //     finally
    //     {
    //         await _documentWriter.SaveAsync(commandDocument with { Payload = commandToSave }, typeof(TAggregatePayload));
    //         if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
    //         {
    //             _semaphoreInMemory.Release();
    //         }
    //     }
    //     return (new CommandExecutorResponse(commandDocument.AggregateId, commandDocument.Id, version, null), events);
    // }
    public async Task<(CommandExecutorResponse, List<IEvent>)> ExecCommandAsync<TAggregatePayload, C>(
        C command,
        List<CallHistory>? callHistories = null) where TAggregatePayload : IAggregatePayload, new()
        where C : ICommandBase<TAggregatePayload>
    {
        var validationResult = command.ValidateProperties()?.ToList();
        if (validationResult?.Any() == true)
        {
            return (new CommandExecutorResponse(null, null, 0, validationResult), new List<IEvent>());
        }
        return await ExecCommandWithoutValidationAsync<TAggregatePayload, C>(command, callHistories);
    }
    public async Task<(CommandExecutorResponse, List<IEvent>)> ExecCommandWithoutValidationAsync<TAggregatePayload, TCommand>(
        TCommand command,
        List<CallHistory>? callHistories = null) where TAggregatePayload : IAggregatePayload, new()
        where TCommand : ICommandBase<TAggregatePayload>
    {
        var commandDocument
            = new CommandDocument<TCommand>(Guid.Empty, command, typeof(TAggregatePayload), callHistories)
            {
                ExecutedUser = _userInformationFactory.GetCurrentUserInformation()
            };
        List<IEvent> events = new();
        var commandToSave = command is ICleanupNecessaryCommand<TCommand> cleanupCommand ? cleanupCommand.CleanupCommandIfNeeded(command) : command;
        var version = 0;
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(typeof(TAggregatePayload));
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            await _semaphoreInMemory.WaitAsync();
        }
        try
        {
            var handler =
                _serviceProvider.GetService(typeof(ICommandHandler<TAggregatePayload, TCommand>)) as
                    ICommandHandler<TAggregatePayload, TCommand>;
            if (handler is null)
            {
                throw new SekibanCommandNotRegisteredException(typeof(TCommand).Name);
            }
            var aggregateId = command.GetAggregateId();
            commandDocument
                = new CommandDocument<TCommand>(aggregateId, command, typeof(TAggregatePayload), callHistories)
                {
                    ExecutedUser = _userInformationFactory.GetCurrentUserInformation()
                };
            if (command is IOnlyPublishingCommand)
            {
                var baseClass = typeof(OnlyPublishingCommandHandlerAdapter<,>);
                var adapterClass = baseClass.MakeGenericType(typeof(TAggregatePayload), typeof(TCommand));
                var adapter = Activator.CreateInstance(adapterClass) ?? throw new Exception("Method not found");
                var method = adapterClass.GetMethod("HandleCommandAsync") ?? throw new Exception("HandleCommandAsync not found");
                var commandResponse =
                    (CommandResponse)await ((dynamic?)method.Invoke(adapter, new object?[] { commandDocument, handler, aggregateId }) ??
                    throw new SekibanCommandHandlerNotMatchException("Command failed to execute " + command.GetType().Name));
                await HandleEventsAsync<TAggregatePayload, TCommand>(commandResponse.Events, commandDocument);
                version = commandResponse.Version;
            }
            else
            {
                var adapter = new CommandHandlerAdapter<TAggregatePayload, TCommand>(aggregateLoader);
                var commandResponse = await adapter.HandleCommandAsync(commandDocument, handler, aggregateId);
                await HandleEventsAsync<TAggregatePayload, TCommand>(commandResponse.Events, commandDocument);
                version = commandResponse.Version;
            }
        }
        catch (Exception e)
        {
            commandDocument = commandDocument with { Exception = SekibanJsonHelper.Serialize(e) };
            throw;
        }
        finally
        {
            await _documentWriter.SaveAsync(commandDocument with { Payload = commandToSave }, typeof(TAggregatePayload));
            if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
            {
                _semaphoreInMemory.Release();
            }
        }
        return (new CommandExecutorResponse(commandDocument.AggregateId, commandDocument.Id, version, null), events);
    }


    private async Task<List<IEvent>> HandleEventsAsync<TAggregatePayload, TCommand>(
        IReadOnlyCollection<IEvent> events,
        CommandDocument<TCommand> commandDocument)
        where TAggregatePayload : IAggregatePayload, new()
        where TCommand : ICommandBase<TAggregatePayload>
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
