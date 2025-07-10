import { AbstractActor, ActorId, DaprClient } from '@dapr/dapr';
import type { IEventStore, PartitionKeys } from '@sekiban/core';
import { EventRetrievalInfo, AggregateGroupStream } from '@sekiban/core';
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
  private eventStore: IEventStore;
  
  constructor(ctx: any, id: any) {
    super(ctx, id);
    // EventStore will be injected via setupDependencies
    this.eventStore = {} as IEventStore;
  }
  
  // Method to inject dependencies after construction
  setupDependencies(eventStore: IEventStore): void {
    this.eventStore = eventStore;
  }
  
  /**
   * Actor activation
   */
  async onActivate(): Promise<void> {
    console.log('[AggregateEventHandlerActor] onActivate called for actor:', (this as any).id?.toString());
    // Load partition info on activation
    await this.loadPartitionInfoAsync();
    console.log('[AggregateEventHandlerActor] Actor activated with partition info:', JSON.stringify(this.partitionInfo));
  }
  
  /**
   * Append events with concurrency check
   */
  async appendEventsAsync(
    expectedLastSortableUniqueId: string,
    events: SerializableEventDocument[]
  ): Promise<EventHandlingResponse> {
    console.log('[AggregateEventHandlerActor] appendEventsAsync called with:');
    console.log('  - Actor ID:', (this as any).id?.toString());
    console.log('  - Expected last ID:', expectedLastSortableUniqueId);
    console.log('  - Events count:', events.length);
    console.log('  - Partition info:', JSON.stringify(this.partitionInfo));
    
    try {
      // Load current state
      const stateManager = await this.getStateManager();
      const [hasState, handlerState] = await stateManager.tryGetState(
        this.HANDLER_STATE_KEY
      );
      
      console.log('[AggregateEventHandlerActor] Current state:', hasState ? 'exists' : 'not found', handlerState);
      
      const currentLastId = (handlerState as any)?.lastSortableUniqueId || '';
      
      // Validate optimistic concurrency
      if (currentLastId !== expectedLastSortableUniqueId) {
        console.error('[AggregateEventHandlerActor] Concurrency conflict:', {
          expected: expectedLastSortableUniqueId,
          actual: currentLastId
        });
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
      await stateManager.setState(this.EVENTS_KEY, allEvents);
      
      // Save to external storage
      if (this.partitionInfo) {
        console.log('[AggregateEventHandlerActor] Saving to event store:', {
          partitionKeys: this.partitionInfo.partitionKeys,
          eventCount: events.length
        });
        
        try {
          const deserializedEvents = events.map(e => this.deserializeEvent(e));
          await this.eventStore.saveEvents(deserializedEvents);
          console.log('[AggregateEventHandlerActor] Events saved to event store successfully');
        } catch (saveError) {
          console.error('[AggregateEventHandlerActor] Failed to save to event store:', saveError);
          throw saveError;
        }
      } else {
        console.warn('[AggregateEventHandlerActor] No partition info available, skipping event store save');
      }
      
      // Update metadata
      const newLastId = events[events.length - 1].sortableUniqueId;
      const newState: AggregateEventHandlerState = {
        lastSortableUniqueId: newLastId,
        eventCount: allEvents.length
      };
      
      await stateManager.setState(this.HANDLER_STATE_KEY, newState);
      
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
    const stateManager = await this.getStateManager();
    const [hasState, handlerState] = await stateManager.tryGetState<AggregateEventHandlerState>(
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
    const stateManager = await this.getStateManager();
    const [hasEvents, events] = await stateManager.tryGetState<SerializableEventDocument[]>(
      this.EVENTS_KEY
    );
    
    if (!hasEvents || !events || (events as any).length === 0) {
      // Fallback to external storage
      if (this.partitionInfo) {
        const eventRetrievalInfo = new EventRetrievalInfo();
        eventRetrievalInfo.aggregateStream = new AggregateGroupStream(this.partitionInfo.partitionKeys);
        const externalEventsResult = await this.eventStore.getEvents(eventRetrievalInfo);
        const externalEvents = externalEventsResult.isOk() ? externalEventsResult.value : [];
        
        const serializedEvents = externalEvents.map((e: any) => this.serializeEvent(e));
        
        // Cache in state for next time
        if (serializedEvents.length > 0) {
          await stateManager.setState(this.EVENTS_KEY, serializedEvents);
          
          // Update metadata
          const lastEvent = serializedEvents[serializedEvents.length - 1];
          const state: AggregateEventHandlerState = {
            lastSortableUniqueId: lastEvent.sortableUniqueId,
            eventCount: serializedEvents.length
          };
          await stateManager.setState(this.HANDLER_STATE_KEY, state);
        }
        
        return serializedEvents;
      }
      
      return [];
    }
    
    return (events as SerializableEventDocument[]) || [];
  }
  
  /**
   * Load partition info from state or actor ID
   */
  private async loadPartitionInfoAsync(): Promise<void> {
    const stateManager = await this.getStateManager();
    const [hasPartitionInfo, partitionInfo] = await stateManager.tryGetState<ActorPartitionInfo>(
      this.PARTITION_INFO_KEY
    );
    
    if (hasPartitionInfo && partitionInfo) {
      this.partitionInfo = partitionInfo;
    } else {
      // Extract from actor ID
      this.partitionInfo = this.getPartitionInfoFromActorId();
      await stateManager.setState(
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
    const actorId = (this as any).id.toString();
    console.log('[AggregateEventHandlerActor] Extracting partition info from actor ID:', actorId);
    const idParts = actorId.split(':');
    
    const partitionInfo = {
      partitionKeys: {
        aggregateId: idParts[1] || '',
        group: idParts[0] || '',
        rootPartitionKey: idParts[2] || 'default'
      } as PartitionKeys,
      aggregateType: idParts[0] || '',
      projectorType: '' // Not used in event handler
    };
    
    console.log('[AggregateEventHandlerActor] Extracted partition info:', JSON.stringify(partitionInfo));
    return partitionInfo;
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