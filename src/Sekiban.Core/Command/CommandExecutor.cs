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
    public async Task<(CommandExecutorResponse, List<IEvent>)> ExecChangeCommandAsync<TAggregatePayload, C>(
        C command,
        List<CallHistory>? callHistories = null) where TAggregatePayload : IAggregatePayload, new()
        where C : ChangeCommandBase<TAggregatePayload>
    {
        var validationResult = command.ValidateProperties()?.ToList();
        if (validationResult?.Any() == true)
        {
            return (new CommandExecutorResponse(null, null, 0, validationResult), new List<IEvent>());
        }
        return await ExecChangeCommandWithoutValidationAsync<TAggregatePayload, C>(command, callHistories);
    }
    public async Task<(CommandExecutorResponse, List<IEvent>)> ExecChangeCommandWithoutValidationAsync<TAggregatePayload, C>(
        C command,
        List<CallHistory>? callHistories = null) where TAggregatePayload : IAggregatePayload, new()
        where C : ChangeCommandBase<TAggregatePayload>
    {
        var commandDocument = new CommandDocument<C>(command.GetAggregateId(), command, typeof(TAggregatePayload), callHistories)
        {
            ExecutedUser = _userInformationFactory.GetCurrentUserInformation()
        };

        List<IEvent> events;
        var version = 0;
        var commandToSave = command;
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(typeof(TAggregatePayload));
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            await _semaphoreInMemory.WaitAsync();
        }
        try
        {
            var handler =
                _serviceProvider.GetService(typeof(IChangeCommandHandler<TAggregatePayload, C>)) as
                    IChangeCommandHandler<TAggregatePayload, C>;
            if (handler is null)
            {
                throw new SekibanCommandNotRegisteredException(typeof(C).Name);
            }
            if (command is not IOnlyPublishingCommand)
            {
                var aggregate = await aggregateLoader.AsAggregateAsync<TAggregatePayload>(command.GetAggregateId());
                if (aggregate is null)
                {
                    throw new SekibanInvalidArgumentException();
                }
                commandToSave = handler.CleanupCommandIfNeeded(commandToSave);
                var result = await handler.HandleAsync(commandDocument, aggregate);
                version = result.Version;
                commandDocument = commandDocument with { AggregateId = result.AggregateId };

                events = await HandleEventsAsync<TAggregatePayload, C>(result.Events, commandDocument);
                if (result is null)
                {
                    throw new SekibanInvalidArgumentException();
                }
            }
            else
            {
                commandToSave = handler.CleanupCommandIfNeeded(commandToSave);
                var result = await handler.HandleForOnlyPublishingCommandAsync(commandDocument, command.GetAggregateId());
                version = result.Version;
                commandDocument = commandDocument with { AggregateId = result.AggregateId };

                events = await HandleEventsAsync<TAggregatePayload, C>(result.Events, commandDocument);
                if (result is null)
                {
                    throw new SekibanInvalidArgumentException();
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
            await _documentWriter.SaveAsync(commandDocument with { Payload = commandToSave }, typeof(TAggregatePayload));
            if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
            {
                _semaphoreInMemory.Release();
            }
        }
        return (new CommandExecutorResponse(commandDocument.AggregateId, commandDocument.Id, version, null), events);
    }
    public async Task<(CommandExecutorResponse, List<IEvent>)> ExecCreateCommandAsync<TAggregatePayload, C>(
        C command,
        List<CallHistory>? callHistories = null) where TAggregatePayload : IAggregatePayload, new()
        where C : ICreateCommand<TAggregatePayload>
    {
        var validationResult = command.ValidateProperties()?.ToList();
        if (validationResult?.Any() == true)
        {
            return (new CommandExecutorResponse(null, null, 0, validationResult), new List<IEvent>());
        }
        return await ExecCreateCommandWithoutValidationAsync<TAggregatePayload, C>(command, callHistories);
    }
    public async Task<(CommandExecutorResponse, List<IEvent>)> ExecCreateCommandWithoutValidationAsync<TAggregatePayload, C>(
        C command,
        List<CallHistory>? callHistories = null) where TAggregatePayload : IAggregatePayload, new()
        where C : ICreateCommand<TAggregatePayload>
    {
        var commandDocument
            = new CommandDocument<C>(Guid.Empty, command, typeof(TAggregatePayload), callHistories)
            {
                ExecutedUser = _userInformationFactory.GetCurrentUserInformation()
            };
        List<IEvent> events = new();
        var commandToSave = command;
        var version = 0;
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(typeof(TAggregatePayload));
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            await _semaphoreInMemory.WaitAsync();
        }
        try
        {
            var handler =
                _serviceProvider.GetService(typeof(ICreateCommandHandler<TAggregatePayload, C>)) as
                    ICreateCommandHandler<TAggregatePayload, C>;
            if (handler is null)
            {
                throw new SekibanCommandNotRegisteredException(typeof(C).Name);
            }
            commandToSave = handler.CleanupCommandIfNeeded(commandToSave);
            var aggregateId = command.GetAggregateId();
            commandDocument
                = new CommandDocument<C>(aggregateId, command, typeof(TAggregatePayload), callHistories)
                {
                    ExecutedUser = _userInformationFactory.GetCurrentUserInformation()
                };
            var aggregate = new Aggregate<TAggregatePayload> { AggregateId = aggregateId };

            var result = await handler.HandleAsync(commandDocument, aggregate);
            version = result.Version;
            if (result.Events.Any())
            {
                foreach (var ev in result.Events)
                {
                    ev.CallHistories.AddRange(commandDocument.GetCallHistoriesIncludesItself());
                }
                events.AddRange(result.Events);
                foreach (var ev in result.Events)
                {
                    await _documentWriter.SaveAndPublishEvent(ev, typeof(TAggregatePayload));
                }
            }
            if (result is null)
            {
                throw new SekibanInvalidArgumentException();
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

    private async Task<List<IEvent>> HandleEventsAsync<TAggregatePayload, C>(
        IReadOnlyCollection<IEvent> events,
        CommandDocument<C> commandDocument)
        where TAggregatePayload : IAggregatePayload, new()
        where C : ChangeCommandBase<TAggregatePayload>
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
