import { AbstractActor, ActorId, DaprClient } from '@dapr/dapr';
import type { 
  IProjector,
  Aggregate,
  EventDocument,
  ICommandExecutor,
  IEventStore,
  PartitionKeys,
  CommandExecutionResult
} from '@sekiban/core';
import { Result, ok, err } from 'neverthrow';
import type { 
  IAggregateActor,
  IAggregateEventHandlerActor,
  SerializableAggregate,
  SerializableCommandAndMetadata,
  ActorPartitionInfo
} from './interfaces';

/**
 * Main actor for handling aggregate state management and command execution
 * Mirrors C# AggregateActor implementation
 */
export class AggregateActor extends AbstractActor implements IAggregateActor {
  private initialized = false;
  private hasUnsavedChanges = false;
  private saveTimer?: NodeJS.Timer;
  private aggregateState?: Aggregate;
  private lastSortableUniqueId?: string;
  private partitionInfo?: ActorPartitionInfo;
  
  // State keys
  private readonly AGGREGATE_STATE_KEY = "aggregateState";
  private readonly PARTITION_INFO_KEY = "partitionInfo";
  
  // Save interval (10 seconds)
  private readonly SAVE_INTERVAL_MS = 10000;
  
  constructor(
    daprClient: DaprClient,
    id: ActorId,
    private readonly projector: IProjector<any>,
    private readonly commandExecutor: ICommandExecutor,
    private readonly eventHandlerActorProxy: IAggregateEventHandlerActor
  ) {
    super(daprClient, id);
  }
  
  /**
   * Actor activation - set up periodic save timer
   */
  async onActivate(): Promise<void> {
    // Set up periodic save timer
    this.saveTimer = setInterval(
      () => this.saveStateCallbackAsync(),
      this.SAVE_INTERVAL_MS
    );
  }
  
  /**
   * Actor deactivation - clean up and save state
   */
  async onDeactivate(): Promise<void> {
    if (this.saveTimer) {
      clearInterval(this.saveTimer);
      this.saveTimer = undefined;
    }
    
    // Save any pending changes
    if (this.hasUnsavedChanges) {
      await this.saveStateAsync();
    }
  }
  
  /**
   * Get current aggregate state
   */
  async getAggregateStateAsync(): Promise<SerializableAggregate> {
    await this.ensureInitializedAsync();
    
    if (!this.aggregateState || !this.partitionInfo) {
      throw new Error('Aggregate state not initialized');
    }
    
    return {
      partitionKeys: this.partitionInfo.partitionKeys,
      aggregate: this.aggregateState,
      lastSortableUniqueId: this.lastSortableUniqueId || ''
    };
  }
  
  /**
   * Execute command and return response as JSON string
   */
  async executeCommandAsync(
    command: SerializableCommandAndMetadata
  ): Promise<string> {
    try {
      await this.ensureInitializedAsync();
      
      // Get current state
      const currentState = await this.getStateInternalAsync();
      
      // Execute command through command executor
      const result = await this.commandExecutor.execute(
        command.command,
        currentState,
        this.projector
      );
      
      if (result.isOk()) {
        const executionResult = result.value;
        
        // Append events if any were produced
        if (executionResult.events.length > 0) {
          const appendResult = await this.eventHandlerActorProxy.appendEventsAsync(
            this.lastSortableUniqueId || '',
            executionResult.events.map(e => this.serializeEvent(e))
          );
          
          if (!appendResult.isSuccess) {
            return JSON.stringify({
              isSuccess: false,
              error: appendResult.error || 'Failed to append events'
            });
          }
          
          // Update local state
          for (const event of executionResult.events) {
            this.aggregateState = this.projector.applyEvent(
              this.aggregateState!,
              event
            );
            this.lastSortableUniqueId = event.sortableUniqueId;
          }
          
          this.hasUnsavedChanges = true;
        }
        
        return JSON.stringify({
          isSuccess: true,
          events: executionResult.events,
          aggregate: this.aggregateState
        });
      } else {
        return JSON.stringify({
          isSuccess: false,
          error: result.error.message
        });
      }
    } catch (error) {
      return JSON.stringify({
        isSuccess: false,
        error: error instanceof Error ? error.message : 'Unknown error'
      });
    }
  }
  
  /**
   * Rebuild state from all events
   */
  async rebuildStateAsync(): Promise<void> {
    const events = await this.eventHandlerActorProxy.getAllEventsAsync();
    
    // Reset state
    this.aggregateState = {
      payload: this.projector.initialState(),
      version: 0,
      lastSortableUniqueId: ''
    } as Aggregate;
    
    // Apply all events
    for (const event of events) {
      this.aggregateState = this.projector.applyEvent(
        this.aggregateState,
        this.deserializeEvent(event)
      );
      this.lastSortableUniqueId = event.sortableUniqueId;
    }
    
    this.hasUnsavedChanges = true;
    await this.saveStateAsync();
  }
  
  /**
   * Timer callback for periodic saving
   */
  async saveStateCallbackAsync(state?: any): Promise<void> {
    if (this.hasUnsavedChanges) {
      await this.saveStateAsync();
    }
  }
  
  /**
   * Reminder handling (IRemindable)
   */
  async receiveReminderAsync(
    reminderName: string,
    state: Buffer,
    dueTime: string,
    period: string
  ): Promise<void> {
    // Handle reminders if needed
    switch (reminderName) {
      case 'save':
        await this.saveStateCallbackAsync();
        break;
    }
  }
  
  /**
   * Ensure actor is initialized
   */
  private async ensureInitializedAsync(): Promise<void> {
    if (!this.initialized) {
      await this.loadStateInternalAsync();
      this.initialized = true;
    }
  }
  
  /**
   * Get internal state
   */
  private async getStateInternalAsync(): Promise<Aggregate | undefined> {
    await this.ensureInitializedAsync();
    return this.aggregateState;
  }
  
  /**
   * Load state with snapshot + delta pattern
   */
  private async loadStateInternalAsync(): Promise<void> {
    // Load partition info
    const [hasPartitionInfo, partitionInfo] = await (this as any).stateManager.tryGetState<ActorPartitionInfo>(
      this.PARTITION_INFO_KEY
    );
    
    if (!hasPartitionInfo) {
      // Extract from actor ID
      this.partitionInfo = this.getPartitionInfoFromActorId();
      await this.savePartitionInfoAsync();
    } else {
      this.partitionInfo = partitionInfo!;
    }
    
    // Try to load snapshot
    const [hasSnapshot, snapshot] = await (this as any).stateManager.tryGetState<SerializableAggregate>(
      this.AGGREGATE_STATE_KEY
    );
    
    if (hasSnapshot && snapshot) {
      // Load delta events since snapshot
      const deltaEvents = await this.eventHandlerActorProxy.getDeltaEventsAsync(
        snapshot.lastSortableUniqueId,
        1000
      );
      
      // Start with snapshot state
      this.aggregateState = snapshot.aggregate;
      this.lastSortableUniqueId = snapshot.lastSortableUniqueId;
      
      // Apply delta events
      for (const event of deltaEvents) {
        this.aggregateState = this.projector.applyEvent(
          this.aggregateState,
          this.deserializeEvent(event)
        );
        this.lastSortableUniqueId = event.sortableUniqueId;
      }
    } else {
      // No snapshot - rebuild from all events
      await this.rebuildStateAsync();
    }
  }
  
  /**
   * Save current state as snapshot
   */
  private async saveStateAsync(): Promise<void> {
    if (!this.aggregateState || !this.partitionInfo) {
      return;
    }
    
    const snapshot: SerializableAggregate = {
      partitionKeys: this.partitionInfo.partitionKeys,
      aggregate: this.aggregateState,
      lastSortableUniqueId: this.lastSortableUniqueId || ''
    };
    
    await (this as any).stateManager.setState(
      this.AGGREGATE_STATE_KEY,
      snapshot
    );
    
    this.hasUnsavedChanges = false;
  }
  
  /**
   * Save partition info
   */
  private async savePartitionInfoAsync(): Promise<void> {
    if (this.partitionInfo) {
      await (this as any).stateManager.setState(
        this.PARTITION_INFO_KEY,
        this.partitionInfo
      );
    }
  }
  
  /**
   * Extract partition info from actor ID
   */
  private getPartitionInfoFromActorId(): ActorPartitionInfo {
    // Actor ID format: "aggregateType:aggregateId:rootPartition"
    const idParts = (this as any).id.toString().split(':');
    
    return {
      partitionKeys: {
        aggregateId: idParts[1] || '',
        group: idParts[0] || '',
        rootPartitionKey: idParts[2] || 'default'
      } as PartitionKeys,
      aggregateType: idParts[0] || '',
      projectorType: this.projector.constructor.name
    };
  }
  
  /**
   * Serialize event for storage
   */
  private serializeEvent(event: EventDocument): SerializableEventDocument {
    return {
      id: event.id,
      sortableUniqueId: event.sortableUniqueId,
      payload: event.payload,
      eventType: event.payload.constructor.name,
      aggregateId: event.aggregateId,
      partitionKeys: event.partitionKeys,
      version: event.version,
      createdAt: event.createdAt.toISOString(),
      metadata: event.metadata
    };
  }
  
  /**
   * Deserialize event from storage
   */
  private deserializeEvent(serialized: SerializableEventDocument): EventDocument {
    return {
      ...serialized,
      createdAt: new Date(serialized.createdAt)
    } as EventDocument;
  }
}