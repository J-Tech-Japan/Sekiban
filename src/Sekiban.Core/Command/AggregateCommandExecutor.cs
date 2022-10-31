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

public class AggregateCommandExecutor : IAggregateCommandExecutor
{
    private static readonly SemaphoreSlim _semaphoreInMemory = new(1, 1);
    private readonly IDocumentWriter _documentWriter;
    private readonly IServiceProvider _serviceProvider;
    private readonly IUserInformationFactory _userInformationFactory;
    private readonly ISingleProjectionService singleProjectionService;

    public AggregateCommandExecutor(
        IDocumentWriter documentWriter,
        IServiceProvider serviceProvider,
        ISingleProjectionService singleProjectionService,
        IUserInformationFactory userInformationFactory)
    {
        _documentWriter = documentWriter;
        _serviceProvider = serviceProvider;
        this.singleProjectionService = singleProjectionService;
        _userInformationFactory = userInformationFactory;
    }
    public async Task<(AggregateCommandExecutorResponse, List<IAggregateEvent>)> ExecChangeCommandAsync<TAggregatePayload, C>(
        C command,
        List<CallHistory>? callHistories = null) where TAggregatePayload : IAggregatePayload, new()
        where C : ChangeAggregateCommandBase<TAggregatePayload>
    {
        var validationResult = command.TryValidateProperties()?.ToList();
        if (validationResult?.Any() == true)
        {
            return (new AggregateCommandExecutorResponse(null, null, 0, validationResult), new List<IAggregateEvent>());
        }
        return await ExecChangeCommandWithoutValidationAsync<TAggregatePayload, C>(command, callHistories);
    }
    public async Task<(AggregateCommandExecutorResponse, List<IAggregateEvent>)> ExecChangeCommandWithoutValidationAsync<TAggregatePayload, C>(
        C command,
        List<CallHistory>? callHistories = null) where TAggregatePayload : IAggregatePayload, new()
        where C : ChangeAggregateCommandBase<TAggregatePayload>
    {
        var commandDocument = new AggregateCommandDocument<C>(command.GetAggregateId(), command, typeof(TAggregatePayload), callHistories)
        {
            ExecutedUser = _userInformationFactory.GetCurrentUserInformation()
        };

        List<IAggregateEvent> events;
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
                _serviceProvider.GetService(typeof(IChangeAggregateCommandHandler<TAggregatePayload, C>)) as
                    IChangeAggregateCommandHandler<TAggregatePayload, C>;
            if (handler is null)
            {
                throw new SekibanAggregateCommandNotRegisteredException(typeof(C).Name);
            }
            if (command is not IOnlyPublishingCommand)
            {
                var aggregate = await singleProjectionService.GetAggregateAsync<TAggregatePayload>(command.GetAggregateId());
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
        return (new AggregateCommandExecutorResponse(commandDocument.AggregateId, commandDocument.Id, version, null), events);
    }
    public async Task<(AggregateCommandExecutorResponse, List<IAggregateEvent>)> ExecCreateCommandAsync<TAggregatePayload, C>(
        C command,
        List<CallHistory>? callHistories = null) where TAggregatePayload : IAggregatePayload, new()
        where C : ICreateAggregateCommand<TAggregatePayload>
    {
        var validationResult = command.TryValidateProperties()?.ToList();
        if (validationResult?.Any() == true)
        {
            return (new AggregateCommandExecutorResponse(null, null, 0, validationResult), new List<IAggregateEvent>());
        }
        return await ExecCreateCommandWithoutValidationAsync<TAggregatePayload, C>(command, callHistories);
    }
    public async Task<(AggregateCommandExecutorResponse, List<IAggregateEvent>)> ExecCreateCommandWithoutValidationAsync<TAggregatePayload, C>(
        C command,
        List<CallHistory>? callHistories = null) where TAggregatePayload : IAggregatePayload, new()
        where C : ICreateAggregateCommand<TAggregatePayload>
    {
        var commandDocument
            = new AggregateCommandDocument<C>(Guid.Empty, command, typeof(TAggregatePayload), callHistories)
            {
                ExecutedUser = _userInformationFactory.GetCurrentUserInformation()
            };
        List<IAggregateEvent> events = new();
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
                _serviceProvider.GetService(typeof(ICreateAggregateCommandHandler<TAggregatePayload, C>)) as
                    ICreateAggregateCommandHandler<TAggregatePayload, C>;
            if (handler is null)
            {
                throw new SekibanAggregateCommandNotRegisteredException(typeof(C).Name);
            }
            commandToSave = handler.CleanupCommandIfNeeded(commandToSave);
            var aggregateId = command.GetAggregateId();
            commandDocument
                = new AggregateCommandDocument<C>(aggregateId, command, typeof(TAggregatePayload), callHistories)
                {
                    ExecutedUser = _userInformationFactory.GetCurrentUserInformation()
                };
            var aggregate = new Aggregate<TAggregatePayload> { AggregateId = aggregateId };

            var result = await handler.HandleAsync(commandDocument, aggregate);
            version = result.Version;
            if (result.Events.Any())
            {
                if (result.Events.Any(
                    ev => (ev == result.Events.First() && !ev.IsAggregateInitialEvent) ||
                        (ev != result.Events.First() && ev.IsAggregateInitialEvent)))
                {
                    throw new SekibanCreateCommandShouldSaveCreateEventFirstException();
                }
                foreach (var ev in result.Events)
                {
                    ev.CallHistories.AddRange(commandDocument.GetCallHistoriesIncludesItself());
                }
                events.AddRange(result.Events);
                foreach (var ev in result.Events)
                {
                    await _documentWriter.SaveAndPublishAggregateEvent(ev, typeof(TAggregatePayload));
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
        return (new AggregateCommandExecutorResponse(commandDocument.AggregateId, commandDocument.Id, version, null), events);
    }

    private async Task<List<IAggregateEvent>> HandleEventsAsync<TAggregatePayload, C>(
        IReadOnlyCollection<IAggregateEvent> events,
        AggregateCommandDocument<C> commandDocument)
        where TAggregatePayload : IAggregatePayload, new()
        where C : ChangeAggregateCommandBase<TAggregatePayload>
    {
        var toReturnEvents = new List<IAggregateEvent>();
        if (events.Any())
        {
            foreach (var ev in events)
            {
                ev.CallHistories.AddRange(commandDocument.GetCallHistoriesIncludesItself());
            }
            toReturnEvents.AddRange(events);
            foreach (var ev in events)
            {
                if (ev.IsAggregateInitialEvent)
                {
                    throw new SekibanChangeCommandShouldNotSaveCreateEventException();
                }
                await _documentWriter.SaveAndPublishAggregateEvent(ev, typeof(TAggregatePayload));
            }
        }
        return toReturnEvents;
    }
}
