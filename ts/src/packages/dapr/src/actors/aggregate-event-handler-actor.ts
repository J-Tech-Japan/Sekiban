import { AbstractActor, ActorId, DaprClient } from '@dapr/dapr';
import type { IEventStore, PartitionKeys } from '@sekiban/core';
import type {
  IAggregateEventHandlerActor,
  SerializableEventDocument,
  EventHandlingResponse,
  AggregateEventHandlerState,
  ActorPartitionInfo
} from './interfaces';

/**
 * Handles event persistence and retrieval for aggregate streams
 * Mirrors C# AggregateEventHandlerActor implementation
 */
export class AggregateEventHandlerActor extends AbstractActor 
  implements IAggregateEventHandlerActor {
  
  // State keys
  private readonly HANDLER_STATE_KEY = "aggregateEventHandler";
  private readonly EVENTS_KEY = "aggregateEventDocuments";
  private readonly PARTITION_INFO_KEY = "partitionInfo";
  
  private partitionInfo?: ActorPartitionInfo;
  
  constructor(
    daprClient: DaprClient,
    id: ActorId,
    private readonly eventStore: IEventStore
  ) {
    super(daprClient, id);
  }
  
  /**
   * Actor activation
   */
  async onActivate(): Promise<void> {
    // Load partition info on activation
    await this.loadPartitionInfoAsync();
  }
  
  /**
   * Append events with concurrency check
   */
  async appendEventsAsync(
    expectedLastSortableUniqueId: string,
    events: SerializableEventDocument[]
  ): Promise<EventHandlingResponse> {
    try {
      // Load current state
      const [hasState, handlerState] = await (this as any).stateManager.tryGetState<AggregateEventHandlerState>(
        this.HANDLER_STATE_KEY
      );
      
      const currentLastId = handlerState?.lastSortableUniqueId || '';
      
      // Validate optimistic concurrency
      if (currentLastId !== expectedLastSortableUniqueId) {
        return {
          isSuccess: false,
          error: `Concurrency conflict: expected ${expectedLastSortableUniqueId}, actual ${currentLastId}`
        };
      }
      
      // Load current events from state
      const currentEvents = await this.loadEventsFromStateAsync();
      
      // Append new events
      const allEvents = [...currentEvents, ...events];
      
      // Save to actor state
      await (this as any).stateManager.setState(this.EVENTS_KEY, allEvents);
      
      // Save to external storage
      if (this.partitionInfo) {
        await this.eventStore.saveEvents(
          this.partitionInfo.partitionKeys,
          events.map(e => this.deserializeEvent(e))
        );
      }
      
      // Update metadata
      const newLastId = events[events.length - 1].sortableUniqueId;
      const newState: AggregateEventHandlerState = {
        lastSortableUniqueId: newLastId,
        eventCount: allEvents.length
      };
      
      await (this as any).stateManager.setState(this.HANDLER_STATE_KEY, newState);
      
      return {
        isSuccess: true,
        lastSortableUniqueId: newLastId
      };
    } catch (error) {
      return {
        isSuccess: false,
        error: error instanceof Error ? error.message : 'Unknown error'
      };
    }
  }
  
  /**
   * Get events after a specific point
   */
  async getDeltaEventsAsync(
    fromSortableUniqueId: string,
    limit: number
  ): Promise<SerializableEventDocument[]> {
    const allEvents = await this.loadEventsFromStateAsync();
    
    // Find the index of the fromSortableUniqueId
    const fromIndex = allEvents.findIndex(
      e => e.sortableUniqueId === fromSortableUniqueId
    );
    
    if (fromIndex === -1) {
      // If not found, return all events (safety fallback)
      return allEvents.slice(0, limit);
    }
    
    // Return events after the specified ID, up to limit
    return allEvents.slice(fromIndex + 1, fromIndex + 1 + limit);
  }
  
  /**
   * Get all events
   */
  async getAllEventsAsync(): Promise<SerializableEventDocument[]> {
    return this.loadEventsFromStateAsync();
  }
  
  /**
   * Get last event ID
   */
  async getLastSortableUniqueIdAsync(): Promise<string> {
    const [hasState, handlerState] = await (this as any).stateManager.tryGetState<AggregateEventHandlerState>(
      this.HANDLER_STATE_KEY
    );
    
    return handlerState?.lastSortableUniqueId || '';
  }
  
  /**
   * Register projector (currently no-op as per C# implementation)
   */
  async registerProjectorAsync(projectorKey: string): Promise<void> {
    // No-op - placeholder for future functionality
  }
  
  /**
   * Load events from state with external storage fallback
   */
  private async loadEventsFromStateAsync(): Promise<SerializableEventDocument[]> {
    const [hasEvents, events] = await (this as any).stateManager.tryGetState<SerializableEventDocument[]>(
      this.EVENTS_KEY
    );
    
    if (!hasEvents || !events || events.length === 0) {
      // Fallback to external storage
      if (this.partitionInfo) {
        const externalEvents = await this.eventStore.loadEvents(
          this.partitionInfo.partitionKeys
        );
        
        const serializedEvents = externalEvents.map(e => this.serializeEvent(e));
        
        // Cache in state for next time
        if (serializedEvents.length > 0) {
          await (this as any).stateManager.setState(this.EVENTS_KEY, serializedEvents);
          
          // Update metadata
          const lastEvent = serializedEvents[serializedEvents.length - 1];
          const state: AggregateEventHandlerState = {
            lastSortableUniqueId: lastEvent.sortableUniqueId,
            eventCount: serializedEvents.length
          };
          await (this as any).stateManager.setState(this.HANDLER_STATE_KEY, state);
        }
        
        return serializedEvents;
      }
      
      return [];
    }
    
    return events;
  }
  
  /**
   * Load partition info from state or actor ID
   */
  private async loadPartitionInfoAsync(): Promise<void> {
    const [hasPartitionInfo, partitionInfo] = await (this as any).stateManager.tryGetState<ActorPartitionInfo>(
      this.PARTITION_INFO_KEY
    );
    
    if (hasPartitionInfo && partitionInfo) {
      this.partitionInfo = partitionInfo;
    } else {
      // Extract from actor ID
      this.partitionInfo = this.getPartitionInfoFromActorId();
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
      projectorType: '' // Not used in event handler
    };
  }
  
  /**
   * Serialize event for storage
   */
  private serializeEvent(event: any): SerializableEventDocument {
    return {
      id: event.id,
      sortableUniqueId: event.sortableUniqueId,
      payload: event.payload,
      eventType: event.payload?.constructor?.name || event.eventType || 'Unknown',
      aggregateId: event.aggregateId,
      partitionKeys: event.partitionKeys,
      version: event.version,
      createdAt: event.createdAt instanceof Date ? event.createdAt.toISOString() : event.createdAt,
      metadata: event.metadata || {}
    };
  }
  
  /**
   * Deserialize event from storage
   */
  private deserializeEvent(serialized: SerializableEventDocument): any {
    return {
      ...serialized,
      createdAt: new Date(serialized.createdAt)
    };
  }
}