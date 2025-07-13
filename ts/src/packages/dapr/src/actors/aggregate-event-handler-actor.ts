import { AbstractActor, ActorId, DaprClient } from '@dapr/dapr';
import type { IEventStore, PartitionKeys } from '@sekiban/core';
import type {
  IAggregateEventHandlerActor,
  SerializableEventDocument,
  EventHandlingResponse,
  AggregateEventHandlerState,
  ActorPartitionInfo
} from './interfaces';
import { getDaprCradle } from '../container/index.js';

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
  
  private getDaprClient(): DaprClient {
    // Get DaprClient from the actor context
    return (this as any).client || new DaprClient();
  }
  
  // Method to inject dependencies after construction
  setupDependencies(eventStore: IEventStore): void {
    this.eventStore = eventStore;
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
      const stateManager = await this.getStateManager();
      const [hasState, handlerState] = await stateManager.tryGetState<AggregateEventHandlerState>(
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
      await stateManager.setState(this.EVENTS_KEY, allEvents);
      
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
      
      await stateManager.setState(this.HANDLER_STATE_KEY, newState);
      
      // Publish events to Dapr pub/sub
      try {
        const cradle = getDaprCradle();
        const daprClient = cradle.daprClient || this.getDaprClient();
        const pubSubName = cradle.configuration?.pubSubName || 'pubsub';
        const topicName = cradle.configuration?.eventTopicName || 'sekiban-events';
        
        // Publish each event
        for (const event of events) {
          // Extract only the C# compatible fields
          const publishEvent = {
            Id: event.Id,
            SortableUniqueId: event.SortableUniqueId,
            Version: event.Version,
            AggregateId: event.AggregateId,
            AggregateGroup: event.AggregateGroup,
            RootPartitionKey: event.RootPartitionKey,
            PayloadTypeName: event.PayloadTypeName,
            TimeStamp: event.TimeStamp,
            PartitionKey: event.PartitionKey,
            CausationId: event.CausationId,
            CorrelationId: event.CorrelationId,
            ExecutedUser: event.ExecutedUser,
            CompressedPayloadJson: event.CompressedPayloadJson,
            PayloadAssemblyVersion: event.PayloadAssemblyVersion
          };
          
          console.log(`[AggregateEventHandlerActor] Publishing event to pub/sub:`, {
            pubSubName,
            topicName,
            eventType: publishEvent.PayloadTypeName,
            aggregateId: publishEvent.AggregateId
          });
          
          await daprClient.pubsub.publish(pubSubName, topicName, publishEvent);
        }
        
        console.log(`[AggregateEventHandlerActor] Published ${events.length} events to pub/sub`);
      } catch (pubsubError) {
        // Log but don't fail - pub/sub is not critical for event storage
        console.error('[AggregateEventHandlerActor] Failed to publish events to pub/sub:', pubsubError);
      }
      
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
    
    if (!hasEvents || !events || events.length === 0) {
      // Fallback to external storage
      if (this.partitionInfo) {
        const externalEvents = await this.eventStore.loadEvents(
          this.partitionInfo.partitionKeys
        );
        
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
    
    return events;
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
    // Convert to proper SerializableEventDocument format for pub/sub
    const payloadJson = JSON.stringify(event.payload || {});
    const payloadBase64 = Buffer.from(payloadJson).toString('base64');
    
    return {
      // Use uppercase field names to match C# format
      Id: event.id?.value || event.id || '',
      SortableUniqueId: event.sortableUniqueId || event.id?.value || '',
      Version: event.version || 1,
      
      // Partition keys
      AggregateId: event.aggregateId || event.partitionKeys?.aggregateId || '',
      AggregateGroup: event.aggregateType || event.partitionKeys?.group || 'default',
      RootPartitionKey: event.partitionKeys?.rootPartitionKey || 'default',
      
      // Event info
      PayloadTypeName: event.payload?.constructor?.name || event.eventType || 'Unknown',
      TimeStamp: event.createdAt instanceof Date ? event.createdAt.toISOString() : event.createdAt,
      PartitionKey: event.partitionKeys?.partitionKey || event.partitionKeys?.toString?.() || '',
      
      // Metadata
      CausationId: event.metadata?.causationId || '',
      CorrelationId: event.metadata?.correlationId || '',
      ExecutedUser: event.metadata?.executedUser || event.metadata?.userId || '',
      
      // Payload (not compressed for now)
      CompressedPayloadJson: payloadBase64,
      
      // Version
      PayloadAssemblyVersion: '0.0.0.0',
      
      // Keep old format fields for backward compatibility
      id: event.id,
      sortableUniqueId: event.sortableUniqueId,
      payload: event.payload,
      eventType: event.payload?.constructor?.name || event.eventType || 'Unknown',
      aggregateId: event.aggregateId,
      partitionKeys: event.partitionKeys,
      version: event.version,
      createdAt: event.createdAt instanceof Date ? event.createdAt.toISOString() : event.createdAt,
      metadata: event.metadata || {}
    } as any;
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