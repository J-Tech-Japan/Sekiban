using Orleans;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Orleans;

/// <summary>
/// Orleans-specific implementation of ISekibanExecutor
/// Uses Orleans grains for distributed command execution
/// </summary>
public class OrleansCommandExecutor : ISekibanExecutor
{
    private readonly IEventStore _eventStore;
    private readonly IActorObjectAccessor _actorAccessor;
    private readonly DcbDomainTypes _domainTypes;
    private readonly GeneralSekibanExecutor _generalExecutor;
    
    public OrleansCommandExecutor(
        IClusterClient clusterClient,
        IEventStore eventStore,
        DcbDomainTypes domainTypes)
    {
        _eventStore = eventStore;
        _domainTypes = domainTypes;
        _actorAccessor = new OrleansActorObjectAccessor(clusterClient, eventStore, domainTypes);
        _generalExecutor = new GeneralSekibanExecutor(eventStore, _actorAccessor, domainTypes);
    }
    
    /// <summary>
    /// Execute a command with its built-in handler
    /// </summary>
    public Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : ICommandWithHandler<TCommand>
    {
        return _generalExecutor.ExecuteAsync(command, cancellationToken);
    }
    
    /// <summary>
    /// Execute a command with a provided handler
    /// </summary>
    public Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
        TCommand command,
        ICommandHandler<TCommand> handler,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand
    {
        return _generalExecutor.ExecuteAsync(command, handler, cancellationToken);
    }
    
    /// <summary>
    /// Execute a command with a handler function
    /// </summary>
    public Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
        TCommand command,
        Func<TCommand, ICommandContext, Task<ResultBox<EventOrNone>>> handlerFunc,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand
    {
        return _generalExecutor.ExecuteAsync(command, handlerFunc, cancellationToken);
    }
    
    /// <summary>
    /// Get the current state for a specific tag state
    /// </summary>
    public Task<ResultBox<TagState>> GetTagStateAsync(TagStateId tagStateId)
    {
        return _generalExecutor.GetTagStateAsync(tagStateId);
    }
}