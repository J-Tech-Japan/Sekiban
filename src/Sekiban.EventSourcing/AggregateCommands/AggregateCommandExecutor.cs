using Sekiban.EventSourcing.AggregateCommands.UserInformations;
using Sekiban.EventSourcing.Queries.SingleAggregates;
using Sekiban.EventSourcing.Shared;
using Sekiban.EventSourcing.Validations;
namespace Sekiban.EventSourcing.AggregateCommands;

public class AggregateCommandExecutor : IAggregateCommandExecutor
{
    private static readonly SemaphoreSlim _semaphoreInMemory = new(1, 1);
    private readonly IDocumentWriter _documentWriter;
    private readonly IServiceProvider _serviceProvider;
    private readonly ISingleAggregateService _singleAggregateService;
    private readonly IUserInformationFactory _userInformationFactory;

    public AggregateCommandExecutor(
        IDocumentWriter documentWriter,
        IServiceProvider serviceProvider,
        ISingleAggregateService singleAggregateService,
        IUserInformationFactory userInformationFactory)
    {
        _documentWriter = documentWriter;
        _serviceProvider = serviceProvider;
        _singleAggregateService = singleAggregateService;
        _userInformationFactory = userInformationFactory;
    }
    public async Task<(AggregateCommandExecutorResponse, List<IAggregateEvent>)> ExecChangeCommandAsync<T, TContents, C>(
        C command,
        List<CallHistory>? callHistories = null) where T : AggregateBase<TContents>
        where TContents : IAggregateContents, new()
        where C : ChangeAggregateCommandBase<T>
    {
        var validationResult = command.TryValidateProperties()?.ToList();
        if (validationResult?.Any() == true)
        {
            return (new AggregateCommandExecutorResponse(null, null, 0, validationResult), new List<IAggregateEvent>());
        }
        return await ExecChangeCommandWithoutValidationAsync<T, TContents, C>(command, callHistories);
    }
    public async Task<(AggregateCommandExecutorResponse, List<IAggregateEvent>)> ExecChangeCommandWithoutValidationAsync<T, TContents, C>(
        C command,
        List<CallHistory>? callHistories = null) where T : AggregateBase<TContents>
        where TContents : IAggregateContents, new()
        where C : ChangeAggregateCommandBase<T>
    {
        var commandDocument = new AggregateCommandDocument<C>(command.GetAggregateId(), command, typeof(T), callHistories)
        {
            ExecutedUser = _userInformationFactory.GetCurrentUserInformation()
        };

        List<IAggregateEvent> events;
        var version = 0;
        var commandToSave = command;
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(typeof(T));
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            await _semaphoreInMemory.WaitAsync();
        }
        try
        {
            var handler = _serviceProvider.GetService(typeof(IChangeAggregateCommandHandler<T, C>)) as IChangeAggregateCommandHandler<T, C>;
            if (handler is null)
            {
                throw new SekibanAggregateCommandNotRegisteredException(typeof(C).Name);
            }
            if (command is not IOnlyPublishingCommand)
            {
                var aggregate = await _singleAggregateService.GetAggregateAsync<T, TContents>(command.GetAggregateId());
                if (aggregate is null)
                {
                    throw new SekibanInvalidArgumentException();
                }
                commandToSave = handler.CleanupCommandIfNeeded(commandToSave);
                aggregate.ResetEventsAndSnapshots();
                var result = await handler.HandleAsync(commandDocument, aggregate);
                version = result.Version;
                commandDocument = commandDocument with { AggregateId = result.AggregateId };

                events = await HandleEventsAsync<T, TContents, C>(result.Events, commandDocument);
                aggregate.ResetEventsAndSnapshots();
                if (result is null)
                {
                    throw new SekibanInvalidArgumentException();
                }
            } else
            {
                commandToSave = handler.CleanupCommandIfNeeded(commandToSave);
                var result = await handler.HandleForOnlyPublishingCommandAsync(commandDocument, command.GetAggregateId());
                version = result.Version;
                commandDocument = commandDocument with { AggregateId = result.AggregateId };

                events = await HandleEventsAsync<T, TContents, C>(result.Events, commandDocument);
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
            await _documentWriter.SaveAsync(commandDocument with { Payload = commandToSave }, typeof(T));
            if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
            {
                _semaphoreInMemory.Release();
            }
        }
        return (new AggregateCommandExecutorResponse(commandDocument.AggregateId, commandDocument.Id, version, null), events);
    }

    public async Task<(AggregateCommandExecutorResponse, List<IAggregateEvent>)> ExecCreateCommandAsync<T, TContents, C>(
        C command,
        List<CallHistory>? callHistories = null) where T : AggregateBase<TContents>, new()
        where TContents : IAggregateContents, new()
        where C : ICreateAggregateCommand<T>
    {
        var validationResult = command.TryValidateProperties()?.ToList();
        if (validationResult?.Any() == true)
        {
            return (new AggregateCommandExecutorResponse(null, null, 0, validationResult), new List<IAggregateEvent>());
        }
        return await ExecCreateCommandWithoutValidationAsync<T, TContents, C>(command, callHistories);
    }
    public async Task<(AggregateCommandExecutorResponse, List<IAggregateEvent>)> ExecCreateCommandWithoutValidationAsync<T, TContents, C>(
        C command,
        List<CallHistory>? callHistories = null) where T : AggregateBase<TContents>, new()
        where TContents : IAggregateContents, new()
        where C : ICreateAggregateCommand<T>
    {
        var commandDocument
            = new AggregateCommandDocument<C>(Guid.Empty, command, typeof(T), callHistories)
            {
                ExecutedUser = _userInformationFactory.GetCurrentUserInformation()
            };
        List<IAggregateEvent> events = new();
        var commandToSave = command;
        var version = 0;
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(typeof(T));
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            await _semaphoreInMemory.WaitAsync();
        }
        try
        {
            var handler = _serviceProvider.GetService(typeof(ICreateAggregateCommandHandler<T, C>)) as ICreateAggregateCommandHandler<T, C>;
            if (handler is null)
            {
                throw new SekibanAggregateCommandNotRegisteredException(typeof(C).Name);
            }
            commandToSave = handler.CleanupCommandIfNeeded(commandToSave);
            var aggregateId = handler.GenerateAggregateId(command);
            commandDocument
                = new AggregateCommandDocument<C>(aggregateId, command, typeof(T), callHistories)
                {
                    ExecutedUser = _userInformationFactory.GetCurrentUserInformation()
                };
            var aggregate = new T { AggregateId = aggregateId };

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
                    await _documentWriter.SaveAndPublishAggregateEvent(ev, typeof(T));
                }
            }
            aggregate.ResetEventsAndSnapshots();
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
            await _documentWriter.SaveAsync(commandDocument with { Payload = commandToSave }, typeof(T));
            if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
            {
                _semaphoreInMemory.Release();
            }
        }
        return (new AggregateCommandExecutorResponse(commandDocument.AggregateId, commandDocument.Id, version, null), events);
    }

    private async Task<List<IAggregateEvent>> HandleEventsAsync<T, TContents, C>(
        IReadOnlyCollection<IAggregateEvent> events,
        AggregateCommandDocument<C> commandDocument) where T : AggregateBase<TContents>
        where TContents : IAggregateContents, new()
        where C : ChangeAggregateCommandBase<T>
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
                await _documentWriter.SaveAndPublishAggregateEvent(ev, typeof(T));
            }
        }
        return toReturnEvents;
    }
}
