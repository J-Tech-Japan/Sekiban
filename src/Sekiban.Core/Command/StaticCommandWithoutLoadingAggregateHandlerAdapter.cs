using Microsoft.Extensions.DependencyInjection;
using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using System.Collections.Immutable;
namespace Sekiban.Core.Command;

public class StaticCommandWithoutLoadingAggregateHandlerAdapter<TAggregatePayload, TCommand>(IServiceProvider serviceProvider)
    : ICommandContextWithoutGetState<TAggregatePayload> where TAggregatePayload : IAggregatePayloadGeneratable<TAggregatePayload>
    where TCommand : ICommandWithHandlerCommon<TAggregatePayload, TCommand>, ICommandWithoutLoadingAggregateCommon
{
    private readonly List<IEvent> _events = [];
    private Guid _aggregateId = Guid.Empty;
    private string _rootPartitionKey = string.Empty;
    public ResultBox<T1> GetRequiredService<T1>() where T1 : class => ResultBox.WrapTry(() => serviceProvider.GetRequiredService<T1>());

    public ResultBox<TwoValues<T1, T2>> GetRequiredService<T1, T2>() where T1 : class where T2 : class =>
        GetRequiredService<T1>().Combine(ResultBox.WrapTry(() => serviceProvider.GetRequiredService<T2>()));

    public ResultBox<ThreeValues<T1, T2, T3>> GetRequiredService<T1, T2, T3>() where T1 : class where T2 : class where T3 : class =>
        GetRequiredService<T1, T2>().Combine(ResultBox.WrapTry(() => serviceProvider.GetRequiredService<T3>()));

    public ResultBox<FourValues<T1, T2, T3, T4>> GetRequiredService<T1, T2, T3, T4>()
        where T1 : class where T2 : class where T3 : class where T4 : class =>
        GetRequiredService<T1, T2, T3>().Combine(ResultBox.WrapTry(() => serviceProvider.GetRequiredService<T4>()));

    public ResultBox<UnitValue> AppendEvent(IEventPayloadApplicableTo<TAggregatePayload> eventPayload) =>
        ResultBox.Start
            .Scan(
                aggregate => _events.Add(
                    EventHelper.GenerateEventToSave<IEventPayloadApplicableTo<TAggregatePayload>, TAggregatePayload>(
                        _aggregateId,
                        _rootPartitionKey,
                        eventPayload)))
            .Remap(_ => UnitValue.None);
    public async Task<CommandResponse> HandleCommandAsync(CommandDocument<TCommand> commandDocument, Guid aggregateId, string rootPartitionKey)
    {
        _rootPartitionKey = rootPartitionKey;
        _aggregateId = aggregateId;
        switch (commandDocument.Payload)
        {
            case ICommandWithHandlerWithoutLoadingAggregate<TAggregatePayload, TCommand> syncCommand:
            {
                var commandType = syncCommand.GetType();
                var method = commandType.GetMethod("HandleCommand");
                if (method is null)
                {
                    throw new SekibanCommandHandlerNotMatchException(
                        commandType.Name + "handler should inherit " + typeof(ICommandWithHandler<,>).Name);
                }
                var result = method.Invoke(null, new object[] { syncCommand, this }) as ResultBox<UnitValue>;
                if (result is null)
                {
                    throw new SekibanCommandHandlerNotMatchException(
                        commandType.Name + "handler should inherit " + typeof(ICommandWithHandler<,>).Name);
                }
                result.UnwrapBox();
                return new CommandResponse(aggregateId, _events.ToImmutableList(), 0, _events.Max(m => m.SortableUniqueId));
            }
            case ICommandWithHandlerWithoutLoadingAggregateAsync<TAggregatePayload, TCommand> asyncCommand:
            {
                var commandType = asyncCommand.GetType();
                var method = commandType.GetMethod("HandleCommandAsync");
                if (method is null)
                {
                    throw new SekibanCommandHandlerNotMatchException(
                        commandType.Name + "handler should inherit " + typeof(ICommandWithHandler<,>).Name);
                }
                var resultTask = method.Invoke(null, new object[] { asyncCommand, this }) as Task<ResultBox<UnitValue>>;
                if (resultTask is null)
                {
                    throw new SekibanCommandHandlerNotMatchException(
                        commandType.Name + "handler should inherit " + typeof(ICommandWithHandler<,>).Name);
                }
                var result = await resultTask;
                result.UnwrapBox();
                return new CommandResponse(aggregateId, _events.ToImmutableList(), 0, _events.Max(m => m.SortableUniqueId));
            }
            default:
                throw new SekibanCommandHandlerNotMatchException(
                    commandDocument.Payload.GetType().Name + "handler should inherit " + typeof(ICommandWithoutLoadingAggregateHandler<,>).Name);
        }
    }
}