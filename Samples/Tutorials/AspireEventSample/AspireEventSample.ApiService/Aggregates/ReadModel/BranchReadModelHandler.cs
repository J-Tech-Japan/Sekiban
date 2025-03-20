using AspireEventSample.ApiService.Aggregates.Branches;
using AspireEventSample.ApiService.Grains;
using AspireEventSample.ReadModels;
using Microsoft.Extensions.Logging;
using Orleans;
using Sekiban.Pure.Events;

namespace AspireEventSample.ApiService.Aggregates.ReadModel;

/// <summary>
/// Branch read model handler
/// </summary>
public class BranchReadModelHandler : IReadModelHandler
{
    private readonly IGrainFactory _grainFactory;
    private readonly IEventContextProvider _eventContextProvider;
    private readonly ILogger<BranchReadModelHandler> _logger;
    
    public BranchReadModelHandler(
        IGrainFactory grainFactory,
        IEventContextProvider eventContextProvider,
        ILogger<BranchReadModelHandler> logger)
    {
        _grainFactory = grainFactory;
        _eventContextProvider = eventContextProvider;
        _logger = logger;
    }
    
    /// <summary>
    /// Handle event
    /// </summary>
    public async Task HandleEventAsync(IEvent @event)
    {
        var eventPayload = @event.GetPayload();
        
        // Switch based on event type
        switch (eventPayload)
        {
            case BranchCreated branchCreated:
                await HandleBranchCreatedAsync(branchCreated);
                break;
                
            case BranchNameChanged branchNameChanged:
                await HandleBranchNameChangedAsync(branchNameChanged);
                break;
                
            // Other event types can be handled here
        }
    }
    
    /// <summary>
    /// Handle BranchCreated event
    /// </summary>
    private async Task HandleBranchCreatedAsync(BranchCreated @event)
    {
        var context = _eventContextProvider.GetCurrentEventContext();
        
        _logger.LogInformation("Processing BranchCreated event for branch {BranchName} with ID {BranchId}",
            @event.Name, context.TargetId);
        
        var entity = new BranchDbRecord
        {
            Id = Guid.NewGuid(),
            TargetId = context.TargetId,
            RootPartitionKey = context.RootPartitionKey,
            AggregateGroup = context.AggregateGroup,
            LastSortableUniqueId = context.SortableUniqueId,
            TimeStamp = DateTime.UtcNow,
            Name = @event.Name,
            Country = @event.Country
        };
        
        var branchWriterGrain = _grainFactory.GetGrain<IBranchEntityPostgresWriterGrain>(context.RootPartitionKey);
        await branchWriterGrain.AddOrUpdateEntityAsync(entity);
    }
    
    /// <summary>
    /// Handle BranchNameChanged event
    /// </summary>
    private async Task HandleBranchNameChangedAsync(BranchNameChanged @event)
    {
        var context = _eventContextProvider.GetCurrentEventContext();
        
        _logger.LogInformation("Processing BranchNameChanged event for branch with ID {BranchId}, new name: {BranchName}",
            context.TargetId, @event.Name);
        
        var branchWriterGrain = _grainFactory.GetGrain<IBranchEntityPostgresWriterGrain>(context.RootPartitionKey);
        var existing = await branchWriterGrain.GetEntityByIdAsync(
            context.RootPartitionKey, 
            context.AggregateGroup, 
            context.TargetId);
            
        if (existing != null)
        {
            existing.LastSortableUniqueId = context.SortableUniqueId;
            existing.TimeStamp = DateTime.UtcNow;
            existing.Name = @event.Name;
            
            await branchWriterGrain.AddOrUpdateEntityAsync(existing);
        }
        else
        {
            _logger.LogWarning("Branch with ID {BranchId} not found when processing BranchNameChanged event",
                context.TargetId);
        }
    }
}
