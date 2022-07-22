using Newtonsoft.Json;
using Sekiban.EventSourcing.Queries.SingleAggregates;
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
        where TContents : IAggregateContents
        where C : ChangeAggregateCommandBase<T>
    {
        AggregateDto<TContents>? aggregateDto = null;
        var commandDocument = new AggregateCommandDocument<C>(
            command,
            new AggregateIdPartitionKeyFactory(command.AggregateId, typeof(T)),
            callHistories) { ExecutedUser = _userInformationFactory.GetCurrentUserInformation() };
        List<IAggregateEvent> events = new();

        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(typeof(T));
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            await _semaphoreInMemory.WaitAsync();
        }
        try
        {
            var handler = _serviceProvider.GetService(typeof(IChangeAggregateCommandHandler<T, C>)) as IChangeAggregateCommandHandler<T, C>;
            if (handler == null)
            {
                throw new SekibanAggregateCommandNotRegisteredException(typeof(C).Name);
            }
            var aggregate = await _singleAggregateService.GetAggregateAsync<T, TContents>(command.AggregateId);
            if (aggregate == null)
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
                    await _documentWriter.SaveAndPublishAggregateEvent(ev, typeof(T));
                }
            }
            aggregate.ResetEventsAndSnapshots();
            if (result == null)
            {
                throw new SekibanInvalidArgumentException();
            }
        }
        catch (Exception e)
        {
            commandDocument = commandDocument with
            {
                Exception = JsonConvert.SerializeObject(e, new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore })
            };
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
        List<CallHistory>? callHistories = null) where T : TransferableAggregateBase<TContents>
        where TContents : IAggregateContents
        where C : ICreateAggregateCommand<T>
    {
        AggregateDto<TContents>? aggregateDto = null;
        var commandDocument
            = new AggregateCommandDocument<C>(command, new CanNotUsePartitionKeyFactory(), callHistories)
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
            if (handler == null)
            {
                throw new SekibanAggregateCommandNotRegisteredException(typeof(C).Name);
            }
            var result = await handler.HandleAsync(commandDocument);
            var partitionKeyFactory = new AggregateIdPartitionKeyFactory(result.Aggregate.AggregateId, typeof(T));
            commandDocument = commandDocument with
            {
                PartitionKey = partitionKeyFactory.GetPartitionKey(commandDocument.DocumentType), AggregateId = result.Aggregate.AggregateId
            };
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
            if (result == null)
            {
                throw new SekibanInvalidArgumentException();
            }
        }
        catch (Exception e)
        {
            commandDocument = commandDocument with
            {
                Exception = JsonConvert.SerializeObject(e, new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore })
            };
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
