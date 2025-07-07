import { 
  PartitionKeys,
  ok,
  err,
  Result
} from '@sekiban/core';
import { createTaskDomainTypes } from '@dapr-sample/domain';
import { PostgresEventStore } from '@sekiban/postgres';

// Import interfaces from the dapr package pattern
interface SerializableEventDocument {
  id: string;
  sortableUniqueId: string;
  payload: any;
  eventType: string;
  aggregateId: string;
  partitionKeys: any;
  version: number;
  createdAt: string;
  metadata: any;
}

interface EventHandlingResponse {
  isSuccess: boolean;
  error?: string;
  lastSortableUniqueId?: string;
}

interface AggregateEventHandlerState {
  lastSortableUniqueId: string;
  eventCount: number;
}

interface ActorPartitionInfo {
  partitionKeys: any; // PartitionKeys instance
  aggregateType: string;
  projectorType: string;
}

// Actor state storage (in-memory for demo, would use Dapr state store in production)
const actorStates = new Map<string, AggregateEventHandlerState>();
const actorEvents = new Map<string, SerializableEventDocument[]>();
const actorPartitionInfo = new Map<string, ActorPartitionInfo>();

/**
 * Event Handler Actor - manages event persistence and retrieval
 * This follows the C# IAggregateEventHandlerActor pattern
 */
class AggregateEventHandlerActor {
  constructor(
    private readonly actorId: string
  ) {}
  
  /**
   * Append events with concurrency check
   */
  async appendEventsAsync(
    expectedLastSortableUniqueId: string,
    events: SerializableEventDocument[]
  ): Promise<EventHandlingResponse> {
    try {
      // Get current state
      const currentState = actorStates.get(this.actorId) || {
        lastSortableUniqueId: '',
        eventCount: 0
      };
      
      // Validate optimistic concurrency
      if (currentState.lastSortableUniqueId !== expectedLastSortableUniqueId) {
        return {
          isSuccess: false,
          error: `Concurrency conflict: expected ${expectedLastSortableUniqueId}, actual ${currentState.lastSortableUniqueId}`
        };
      }
      
      // Load current events from state
      const currentEvents = actorEvents.get(this.actorId) || [];
      
      // Append new events
      const allEvents = [...currentEvents, ...events];
      actorEvents.set(this.actorId, allEvents);
      
      // Update state
      const newLastId = events[events.length - 1].sortableUniqueId;
      const newState: AggregateEventHandlerState = {
        lastSortableUniqueId: newLastId,
        eventCount: allEvents.length
      };
      actorStates.set(this.actorId, newState);
      
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
    const allEvents = actorEvents.get(this.actorId) || [];
    
    const fromIndex = allEvents.findIndex(
      e => e.sortableUniqueId === fromSortableUniqueId
    );
    
    if (fromIndex === -1) {
      return allEvents.slice(0, limit);
    }
    
    return allEvents.slice(fromIndex + 1, fromIndex + 1 + limit);
  }
  
  /**
   * Get all events
   */
  async getAllEventsAsync(): Promise<SerializableEventDocument[]> {
    return actorEvents.get(this.actorId) || [];
  }
  
  /**
   * Get last event ID
   */
  async getLastSortableUniqueIdAsync(): Promise<string> {
    const state = actorStates.get(this.actorId);
    return state?.lastSortableUniqueId || '';
  }
}

/**
 * Aggregate Actor - manages aggregate state and command processing
 * This follows the C# IAggregateActor pattern
 */
export class AggregateActor {
  private readonly domainTypes = createTaskDomainTypes();
  private readonly eventHandler: AggregateEventHandlerActor;
  
  constructor(
    private readonly actorId: string
  ) {
    this.eventHandler = new AggregateEventHandlerActor(actorId);
  }
  
  /**
   * Execute command and persist events using the event handler
   */
  async executeCommandAsync(methodData: any): Promise<any> {
    const { commandType, commandData, partitionKeys, metadata } = methodData;
    
    console.log(`Actor ${this.actorId} executing command ${commandType}`);
    
    try {
      // Get command handler
      const handler = this.getCommandHandler(commandType);
      if (!handler) {
        throw new Error(`Unknown command type: ${commandType}`);
      }
      
      // Load current aggregate state
      const currentState = await this.buildAggregateState(partitionKeys);
      
      // Create command context
      const context = this.createCommandContext(currentState, partitionKeys, metadata);
      
      // Execute command handler to get events
      const result = handler(commandData, context);
      if (result.isErr()) {
        throw new Error(result.error.message);
      }
      
      const eventPayloads = result.value;
      
      // Convert event payloads to serializable events
      const events: SerializableEventDocument[] = [];
      let version = currentState.version + 1;
      
      for (const payload of eventPayloads) {
        const eventId = crypto.randomUUID();
        const event: SerializableEventDocument = {
          id: eventId,
          sortableUniqueId: eventId,
          payload,
          eventType: this.getEventType(commandType),
          aggregateId: partitionKeys.aggregateId,
          partitionKeys,
          version: version++,
          createdAt: new Date().toISOString(),
          metadata: metadata || {}
        };
        events.push(event);
      }
      
      // Get current last event ID for concurrency check
      const currentLastId = await this.eventHandler.getLastSortableUniqueIdAsync();
      
      // Append events using the event handler
      const appendResult = await this.eventHandler.appendEventsAsync(currentLastId, events);
      
      if (!appendResult.isSuccess) {
        throw new Error(appendResult.error || 'Failed to append events');
      }
      
      return {
        aggregateId: partitionKeys.aggregateId,
        success: true,
        lastSortableUniqueId: appendResult.lastSortableUniqueId,
        message: `Command ${commandType} processed successfully`
      };
      
    } catch (error) {
      console.error(`Error executing command ${commandType}:`, error);
      return {
        success: false,
        error: error instanceof Error ? error.message : 'Unknown error'
      };
    }
  }
  
  /**
   * Query aggregate state by rebuilding from events
   */
  async queryAsync(methodData: any): Promise<any> {
    const { queryType, taskId } = methodData;
    
    console.log(`Actor ${this.actorId} executing query ${queryType}`);
    
    try {
      // For demo, create partition keys from taskId
      const partitionKeys = PartitionKeys.existing('Task', taskId || this.extractAggregateId());
      
      // Build current state from events
      const aggregateState = await this.buildAggregateState(partitionKeys);
      
      return {
        payload: aggregateState.payload,
        version: aggregateState.version,
        lastSortableUniqueId: aggregateState.lastSortableUniqueId
      };
      
    } catch (error) {
      console.error(`Error executing query:`, error);
      // Return default state
      return {
        payload: {
          aggregateType: 'Task',
          taskId: taskId || crypto.randomUUID(),
          title: 'Test Task',
          description: 'Task data',
          status: 'pending',
          priority: 'medium',
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString()
        },
        version: 0,
        lastSortableUniqueId: ''
      };
    }
  }
  
  /**
   * Get all events for this aggregate
   */
  async getAllEventsAsync(): Promise<SerializableEventDocument[]> {
    return this.eventHandler.getAllEventsAsync();
  }
  
  /**
   * Get events after a specific ID
   */
  async getDeltaEventsAsync(fromSortableUniqueId: string, limit: number): Promise<SerializableEventDocument[]> {
    return this.eventHandler.getDeltaEventsAsync(fromSortableUniqueId, limit);
  }
  
  /**
   * Get last event ID
   */
  async getLastSortableUniqueIdAsync(): Promise<string> {
    return this.eventHandler.getLastSortableUniqueIdAsync();
  }
  
  // Helper methods
  
  private async buildAggregateState(partitionKeys: any): Promise<any> {
    // Get all events from event handler
    const events = await this.eventHandler.getAllEventsAsync();
    
    // Get the projector
    const projector = this.getTaskProjector();
    
    // Apply events to build current state
    let aggregateState = projector.initialState();
    let version = 0;
    let lastSortableUniqueId = '';
    
    for (const event of events) {
      // Apply event to state
      aggregateState = projector.applyEvent(aggregateState, event);
      version = event.version;
      lastSortableUniqueId = event.sortableUniqueId;
    }
    
    return {
      payload: aggregateState,
      version,
      lastSortableUniqueId,
      eventCount: events.length
    };
  }
  
  private createCommandContext(currentState: any, partitionKeys: any, metadata: any): any {
    const events: any[] = [];
    
    return {
      aggregateId: partitionKeys.aggregateId,
      aggregate: currentState.eventCount > 0 ? currentState.payload : undefined,
      getAggregate: () => ok({ payload: currentState.payload }),
      appendEvent: (eventPayload: any) => {
        events.push(eventPayload);
      },
      getService: <T>(serviceType: { new(...args: any[]): T }): T | undefined => undefined,
      events,
      getPartitionKeys: () => partitionKeys,
      getNextVersion: () => currentState.version + events.length + 1,
      getCurrentVersion: () => currentState.version
    };
  }
  
  private extractAggregateId(): string {
    // Extract aggregate ID from actor ID
    // Format: "rootPartition@aggregateId@aggregateType=Object"
    const parts = this.actorId.split('@');
    return parts[1] || crypto.randomUUID();
  }
  
  // Get event type from command type
  private getEventType(commandType: string): string {
    switch (commandType) {
      case 'CreateTask': return 'TaskCreated';
      case 'AssignTask': return 'TaskAssigned';
      case 'CompleteTask': return 'TaskCompleted';
      default: return commandType;
    }
  }
  
  // Simple command handler mapping
  private getCommandHandler(commandType: string): ((data: any, context: any) => Result<any[], any>) | null {
    switch (commandType) {
      case 'CreateTask':
        return (data, context) => {
          if (context.aggregate) {
            return err({ message: 'Task already exists' });
          }
          return ok([{
            taskId: data.taskId,
            title: data.title,
            description: data.description,
            priority: data.priority || 'medium',
            createdAt: new Date().toISOString()
          }]);
        };
        
      case 'AssignTask':
        return (data, context) => {
          if (!context.aggregate) {
            return err({ message: 'Task not found' });
          }
          return ok([{
            taskId: data.taskId,
            assignedTo: data.assignedTo,
            assignedAt: new Date().toISOString()
          }]);
        };
        
      case 'CompleteTask':
        return (data, context) => {
          if (!context.aggregate) {
            return err({ message: 'Task not found' });
          }
          return ok([{
            taskId: data.taskId,
            completedAt: new Date().toISOString()
          }]);
        };
        
      default:
        return null;
    }
  }
  
  // Simple task projector
  private getTaskProjector() {
    return {
      initialState: () => ({
        aggregateType: 'Task',
        taskId: '',
        title: '',
        description: '',
        status: 'pending',
        priority: 'medium',
        assignedTo: null,
        createdAt: null,
        updatedAt: null,
        completedAt: null
      }),
      
      applyEvent: (state: any, event: SerializableEventDocument) => {
        const payload = event.payload;
        
        switch (event.eventType) {
          case 'TaskCreated':
            return {
              ...state,
              taskId: payload.taskId,
              title: payload.title,
              description: payload.description,
              priority: payload.priority,
              createdAt: payload.createdAt,
              status: 'pending'
            };
            
          case 'TaskAssigned':
            return {
              ...state,
              assignedTo: payload.assignedTo,
              status: 'assigned',
              updatedAt: payload.assignedAt
            };
            
          case 'TaskCompleted':
            return {
              ...state,
              status: 'completed',
              completedAt: payload.completedAt,
              updatedAt: payload.completedAt
            };
            
          default:
            return state;
        }
      }
    };
  }
}

// Factory to create actor instances
export async function createActor(actorId: string): Promise<AggregateActor> {
  return new AggregateActor(actorId);
}