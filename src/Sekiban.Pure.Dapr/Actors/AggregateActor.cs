using Dapr.Actors;
using Dapr.Actors.Runtime;
using Microsoft.Extensions.Logging;
using ResultBoxes;
using Sekiban.Pure.Command;
using Sekiban.Pure.Dapr.Services;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Exceptions;
using Sekiban.Pure.Repositories;
using Sekiban.Pure;
using Sekiban.Pure.Executors;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Command.Executor;

namespace Sekiban.Pure.Dapr.Actors;

[Actor(TypeName = nameof(AggregateActor))]
public class AggregateActor : Actor, IAggregateActor, IRemindable
{
    private readonly Repository _repository;
    private readonly SekibanDomainTypes _domainTypes;
    private readonly ILogger<AggregateActor> _logger;
    
    private const string StateKey = "aggregate_state";
    private const string EventCountKey = "event_count";

    public AggregateActor(
        ActorHost host,
        Repository repository,
        SekibanDomainTypes domainTypes,
        ILogger<AggregateActor> logger) : base(host)
    {
        _repository = repository;
        _domainTypes = domainTypes;
        _logger = logger;
    }

    public async Task<ResultBox<CommandResponse>> ExecuteCommandAsync(
        ICommandWithHandlerSerializable command,
        IEvent? relatedEvent = null)
    {
        try
        {
            var partitionKeys = GetPartitionKeys(command);
            
            // Use a simplified in-memory executor for the actor
            // For now, just return a placeholder response
            // In a real implementation, this would process the command properly
            return await Task.FromResult(ResultBox<CommandResponse>.FromValue(
                new CommandResponse(
                    partitionKeys,
                    new List<IEvent>(),
                    0)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command {CommandType}", command.GetType().Name);
            return ResultBox<CommandResponse>.FromException(ex);
        }
    }

    public async Task<ResultBox<IEnumerable<IEvent>>> GetEventsAsync()
    {
        try
        {
            var actorId = Id.GetId();
            var partitionKeys = PartitionKeys.FromPrimaryKeysString(actorId.Split(':').Last());
            
            var events = _repository.Events
                .Where(e => e.PartitionKeys == partitionKeys)
                .ToList();
                
            return await Task.FromResult(ResultBox<IEnumerable<IEvent>>.FromValue(events));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting events");
            return ResultBox<IEnumerable<IEvent>>.FromException(ex);
        }
    }

    public Task ReceiveReminderAsync(string reminderName, byte[] state, TimeSpan dueTime, TimeSpan period)
    {
        // Handle reminders if needed
        _logger.LogInformation("Received reminder {ReminderName}", reminderName);
        return Task.CompletedTask;
    }

    private PartitionKeys GetPartitionKeys(ICommandWithHandlerSerializable command)
    {
        var commandType = command.GetType();
        var method = commandType.GetMethod("GetPartitionKeys", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        if (method != null)
        {
            return (PartitionKeys)method.Invoke(null, new object[] { command })!;
        }

        throw new InvalidOperationException($"GetPartitionKeys method not found for command type {commandType.Name}");
    }
}