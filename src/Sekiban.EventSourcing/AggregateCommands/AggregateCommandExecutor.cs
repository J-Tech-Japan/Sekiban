using Sekiban.EventSourcing.AggregateCommands.UserInformations;
using Sekiban.EventSourcing.Queries.SingleAggregates;
using Sekiban.EventSourcing.Shared;
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
    public async Task<AggregateCommandExecutorResponse<TContents, C>> ExecChangeCommandAsync<T, TContents, C>(
        C command,
        List<CallHistory>? callHistories = null) where T : TransferableAggregateBase<TContents>
        where TContents : IAggregateContents, new()
        where C : ChangeAggregateCommandBase<T>
    {
        AggregateDto<TContents>? aggregateDto = null;
        var commandDocument = new AggregateCommandDocument<C>(command.GetAggregateId(), command, typeof(T), callHistories)
        {
            ExecutedUser = _userInformationFactory.GetCurrentUserInformation()
        };

        List<IAggregateEvent> events = new();

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
            var aggregate = await _singleAggregateService.GetAggregateAsync<T, TContents>(command.GetAggregateId());
            if (aggregate is null)
            {
                throw new SekibanInvalidArgumentException();
            }
            aggregate.ResetEventsAndSnapshots();
            var result = await handler.HandleAsync(commandDocument, aggregate);
            commandDocument = commandDocument with { AggregateId = result.Aggregate.AggregateId };

            aggregateDto = result.Aggregate.ToDto();
            if (result.Aggregate.Events.Any())
            {
                foreach (var ev in result.Aggregate.Events)
                {
                    ev.CallHistories.AddRange(commandDocument.GetCallHistoriesIncludesItself());
                }
                events.AddRange(result.Aggregate.Events);
                foreach (var ev in result.Aggregate.Events)
                {
                    if (ev.IsAggregateInitialEvent)
                    {
                        throw new SekibanChangeCommandShouldNotSaveCreateEventException();
                    }
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
            await _documentWriter.SaveAsync(commandDocument, typeof(T));
            if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
            {
                _semaphoreInMemory.Release();
            }
        }
        return new AggregateCommandExecutorResponse<TContents, C>(commandDocument) { AggregateDto = aggregateDto, Events = events };
    }

    public async Task<AggregateCommandExecutorResponse<TContents, C>> ExecCreateCommandAsync<T, TContents, C>(
        C command,
        List<CallHistory>? callHistories = null) where T : TransferableAggregateBase<TContents>, new()
        where TContents : IAggregateContents, new()
        where C : ICreateAggregateCommand<T>
    {
        AggregateDto<TContents>? aggregateDto = null;
        var commandDocument
            = new AggregateCommandDocument<C>(Guid.Empty, command, typeof(T), callHistories)
            {
                ExecutedUser = _userInformationFactory.GetCurrentUserInformation()
            };
        List<IAggregateEvent> events = new();

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
            var aggregateId = handler.GenerateAggregateId(command);
            commandDocument
                = new AggregateCommandDocument<C>(aggregateId, command, typeof(T), callHistories)
                {
                    ExecutedUser = _userInformationFactory.GetCurrentUserInformation()
                };
            var aggregate = new T { AggregateId = aggregateId };

            var result = await handler.HandleAsync(commandDocument, aggregate);
            aggregateDto = result.Aggregate.ToDto();
            if (result.Aggregate.Events.Any())
            {
                foreach (var ev in result.Aggregate.Events)
                {
                    ev.CallHistories.AddRange(commandDocument.GetCallHistoriesIncludesItself());
                }
                events.AddRange(result.Aggregate.Events);
                foreach (var ev in result.Aggregate.Events)
                {
                    await _documentWriter.SaveAndPublishAggregateEvent(ev, typeof(T));
                }
            }
            result.Aggregate.ResetEventsAndSnapshots();
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
            await _documentWriter.SaveAsync(commandDocument, typeof(T));
            if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
            {
                _semaphoreInMemory.Release();
            }
        }
        return new AggregateCommandExecutorResponse<TContents, C>(commandDocument) { AggregateDto = aggregateDto, Events = events };
    }
}
