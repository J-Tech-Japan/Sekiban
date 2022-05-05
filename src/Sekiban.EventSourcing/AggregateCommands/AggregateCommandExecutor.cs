using Newtonsoft.Json;
using Sekiban.EventSourcing.Snapshots.SnapshotManagers;
using Sekiban.EventSourcing.Snapshots.SnapshotManagers.Commands;
using Sekiban.EventSourcing.Snapshots.SnapshotManagers.Events;
namespace Sekiban.EventSourcing.AggregateCommands;

public class AggregateCommandExecutor
{
    private static readonly SemaphoreSlim _semaphoreInMemory = new(1, 1);
    private readonly IDocumentWriter _documentWriter;
    private readonly IDocumentPersistentRepository _documentPersistentRepository;
    private readonly IServiceProvider _serviceProvider;
    private readonly SingleAggregateService _singleAggregateService;
    private readonly IUserInformationFactory _userInformationFactory;

    public AggregateCommandExecutor(
        IDocumentWriter documentWriter,
        IServiceProvider serviceProvider,
        SingleAggregateService singleAggregateService,
        IUserInformationFactory userInformationFactory, IDocumentPersistentRepository documentPersistentRepository)
    {
        _documentWriter = documentWriter;
        _serviceProvider = serviceProvider;
        _singleAggregateService = singleAggregateService;
        _userInformationFactory = userInformationFactory;
        _documentPersistentRepository = documentPersistentRepository;
    }
    public async Task<AggregateCommandExecutorResponse<Q, C>> ExecChangeCommandAsync<T, Q, C>(
        C command,
        List<CallHistory>? callHistories = null)
        where T : TransferableAggregateBase<Q>
        where Q : AggregateDtoBase, new()
        where C : ChangeAggregateCommandBase<T>
    {
        var toReturn =
            new AggregateCommandExecutorResponse<Q, C>(
                new AggregateCommandDocument<C>(
                    command,
                    new AggregateIdPartitionKeyFactory(command.AggregateId, typeof(T)),
                    callHistories
                ));
        var aggregateContainerGroup =
            AggregateContainerGroupAttribute.FindAggregateContainerGroup(typeof(T));
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            await _semaphoreInMemory.WaitAsync();
        }
        try
        {
            toReturn.Command.ExecutedUser = _userInformationFactory.GetCurrentUserInformation();
            var handler =
                _serviceProvider.GetService(typeof(IChangeAggregateCommandHandler<T, C>)) as
                    IChangeAggregateCommandHandler<T, C>;
            if (handler == null)
            {
                throw new JJAggregateCommandNotRegisteredException(typeof(C).Name);
            }
            var aggregate =
                await _singleAggregateService.GetAggregateAsync<T, Q>(
                    command.AggregateId);
            if (aggregate == null)
            {
                throw new JJInvalidArgumentException();
            }
            aggregate.ResetEventsAndSnapshots();
            var result = await handler.HandleAsync(toReturn.Command, aggregate);
            toReturn.Command.AggregateId = result.Aggregate.AggregateId;

            toReturn.AggregateDto = result.Aggregate.ToDto();
            if (result.Aggregate.Events.Any())
            {
                foreach (var ev in result.Aggregate.Events)
                {
                    ev.CallHistories.AddRange(toReturn.Command.GetCallHistoriesIncludesItself());
                }
                toReturn.Events.AddRange(result.Aggregate.Events);
                foreach (var ev in result.Aggregate.Events)
                {
                    await _documentWriter.SaveAndPublishAggregateEvent(ev, typeof(T));
                    if (aggregateContainerGroup != AggregateContainerGroup.InMemoryContainer)
                    {
                        var snapshotManagerResponse =
                            await ExecChangeCommandAsync<SnapshotManager, SnapshotManagerDto,
                                ReportAggregateVersionToSnapshotManger>(
                                new ReportAggregateVersionToSnapshotManger(
                                    SnapshotManager.SharedId,
                                    typeof(T),
                                    ev.AggregateId,
                                    ev.Version,
                                    null));
                        if (snapshotManagerResponse.Events.Any(
                            m => m.DocumentTypeName == nameof(SnapshotManagerSnapshotTaken)))
                        {
                            foreach (var taken in snapshotManagerResponse.Events.Where(
                                    m => m.DocumentTypeName ==
                                        nameof(SnapshotManagerSnapshotTaken))
                                .Select(m => (SnapshotManagerSnapshotTaken)m))
                            {
                                if (! await _documentPersistentRepository.ExistsSnapshotForAggregateAsync(command.AggregateId, typeof(T), taken.NextSnapshotVersion))
                                {
                                    var aggregateToSnapshot =
                                        await _singleAggregateService
                                            .GetAggregateDtoAsync<T, Q>(
                                                command.AggregateId,
                                                taken.NextSnapshotVersion);
                                    if (aggregateToSnapshot == null)
                                    {
                                        continue;
                                    }
                                    if (taken.NextSnapshotVersion != aggregateToSnapshot.Version) { continue; }
                                    var snapshotDocument = new SnapshotDocument(
                                        new AggregateIdPartitionKeyFactory(ev.AggregateId, typeof(T)),
                                        typeof(T).Name,
                                        aggregateToSnapshot,
                                        ev.AggregateId,
                                        aggregateToSnapshot.LastEventId,
                                        aggregateToSnapshot.LastSortableUniqueId,
                                        aggregateToSnapshot.Version);
                                    await _documentWriter.SaveAsync(snapshotDocument, typeof(T));
                                }
                            }
                        }
                    }
                }
            }
            aggregate.ResetEventsAndSnapshots();
            if (result == null)
            {
                throw new JJInvalidArgumentException();
            }
        }
        catch (Exception e)
        {
            toReturn.Command.Exception = JsonConvert.SerializeObject(
                e,
                new JsonSerializerSettings
                {
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                });
            throw;
        }
        finally
        {
            await _documentWriter.SaveAsync(toReturn.Command, typeof(T));
            if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
            {
                _semaphoreInMemory.Release();
            }
        }
        return toReturn;
    }

    public async Task<AggregateCommandExecutorResponse<Q, C>> ExecCreateCommandAsync<T, Q, C>(
        C command,
        List<CallHistory>? callHistories = null)
        where T : TransferableAggregateBase<Q>
        where Q : AggregateDtoBase, new()
        where C : ICreateAggregateCommand<T>
    {
        var toReturn =
            new AggregateCommandExecutorResponse<Q, C>(
                new AggregateCommandDocument<C>(
                    command,
                    new CanNotUsePartitionKeyFactory(),
                    callHistories
                ));
        var aggregateContainerGroup =
            AggregateContainerGroupAttribute.FindAggregateContainerGroup(typeof(T));
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            await _semaphoreInMemory.WaitAsync();
        }
        try
        {
            toReturn.Command.ExecutedUser = _userInformationFactory.GetCurrentUserInformation();
            var handler =
                _serviceProvider.GetService(
                        typeof(ICreateAggregateCommandHandler<T, C>)) as
                    ICreateAggregateCommandHandler<T, C>;
            if (handler == null)
            {
                throw new JJAggregateCommandNotRegisteredException(typeof(C).Name);
            }
            var result = await handler.HandleAsync(toReturn.Command);
            toReturn.Command.SetPartitionKey(
                new AggregateIdPartitionKeyFactory(result.Aggregate.AggregateId, typeof(T)));
            toReturn.Command.AggregateId = result.Aggregate.AggregateId;
            toReturn.AggregateDto = result.Aggregate.ToDto();
            if (result.Aggregate.Events.Any())
            {
                foreach (var ev in result.Aggregate.Events)
                {
                    ev.CallHistories.AddRange(toReturn.Command.GetCallHistoriesIncludesItself());
                }
                toReturn.Events.AddRange(result.Aggregate.Events);
                foreach (var ev in result.Aggregate.Events)
                {
                    await _documentWriter.SaveAndPublishAggregateEvent(ev, typeof(T));
                }
            }
            result.Aggregate.ResetEventsAndSnapshots();
            if (result == null)
            {
                throw new JJInvalidArgumentException();
            }
        }
        catch (Exception e)
        {
            toReturn.Command.Exception = JsonConvert.SerializeObject(
                e,
                new JsonSerializerSettings
                {
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                });
            throw;
        }
        finally
        {
            await _documentWriter.SaveAsync(toReturn.Command, typeof(T));
            if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
            {
                _semaphoreInMemory.Release();
            }
        }
        return toReturn;
    }
}
