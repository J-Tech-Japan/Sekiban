using Newtonsoft.Json;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.Documents;
using Sekiban.EventSourcing.Histories;
using Sekiban.EventSourcing.Partitions;
using Sekiban.EventSourcing.Partitions.AggregateIdPartitions;
using Sekiban.EventSourcing.Queries;
using Sekiban.EventSourcing.Shared.Exceptions;
namespace Sekiban.EventSourcing.AggregateCommands;

public class AggregateCommandExecutor
{
    private readonly IDocumentWriter _documentWriter;
    private readonly IServiceProvider _serviceProvider;
    private readonly SingleAggregateService _singleAggregateService;
    private readonly IUserInformationFactory _userInformationFactory;

    public AggregateCommandExecutor(
        IDocumentWriter documentWriter,
        IServiceProvider serviceProvider,
        SingleAggregateService singleAggregateService,
        IUserInformationFactory userInformationFactory)
    {
        _documentWriter = documentWriter;
        _serviceProvider = serviceProvider;
        _singleAggregateService = singleAggregateService;
        _userInformationFactory = userInformationFactory;
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
                await _singleAggregateService
                    .GetAggregateAsync<T, Q>(
                        command.AggregateId);
            if (aggregate == null)
            {
                throw new JJInvalidArgumentException();
            }
            aggregate.ResetEventsAndSnepshots();
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
                }
            }
            if (result.Aggregate.Snapshots.Any())
            {
                foreach (var snapshot in result.Aggregate.Snapshots)
                {
                    await _documentWriter.SaveAsync(snapshot, typeof(T));
                }
            }
            if (result == null)
            {
                throw new JJInvalidArgumentException();
            }
        }
        catch (Exception e)
        {
            toReturn.Command.Exception = JsonConvert.SerializeObject(e);
            throw;
        }
        finally
        {
            await _documentWriter.SaveAsync(toReturn.Command, typeof(T));
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
            if (result.Aggregate.Snapshots.Any())
            {
                foreach (var snapshot in result.Aggregate.Snapshots)
                {
                    await _documentWriter.SaveAsync(snapshot, typeof(T));
                }
            }
            if (result == null)
            {
                throw new JJInvalidArgumentException();
            }
        }
        catch (Exception e)
        {
            toReturn.Command.Exception = JsonConvert.SerializeObject(e);
            throw;
        }
        finally
        {
            await _documentWriter.SaveAsync(toReturn.Command, typeof(T));
        }
        return toReturn;
    }
}
