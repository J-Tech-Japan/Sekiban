import { 
  PartitionKeys,
  ok,
  err,
  Result
} from '@sekiban/core';
import { PostgresEventStore } from '@sekiban/postgres';
import { Pool } from 'pg';

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

// Create PostgreSQL connection pool
const pool = new Pool({
  host: process.env.POSTGRES_HOST || 'localhost',
  port: parseInt(process.env.POSTGRES_PORT || '5432'),
  database: process.env.POSTGRES_DB || 'sekiban_events',
  user: process.env.POSTGRES_USER || 'sekiban',
  password: process.env.POSTGRES_PASSWORD || 'sekiban_password'
});

// Create event store instance
const eventStore = new PostgresEventStore(pool);

/**
 * Event Handler Actor - manages event persistence and retrieval using PostgreSQL
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
      // Get current last event ID from database
      const currentLastId = await this.getLastSortableUniqueIdFromDb();
      
      // Validate optimistic concurrency
      if (currentLastId !== expectedLastSortableUniqueId) {
        return {
          isSuccess: false,
          error: `Concurrency conflict: expected ${expectedLastSortableUniqueId}, actual ${currentLastId}`
        };
      }
      
      // Convert serializable events to database format
      const dbEvents = events.map(event => this.toDbEvent(event));
      
      // Save events to PostgreSQL
      console.log('Saving events:', JSON.stringify(dbEvents, null, 2));
      try {
        const saveResult = await eventStore.saveEvents(dbEvents);
        if (saveResult.isErr()) {
          console.error('Save error:', saveResult.error);
          console.error('Full error details:', saveResult.error);
          return {
            isSuccess: false,
            error: saveResult.error.message
          };
        }
      } catch (directError) {
        console.error('Direct save error:', directError);
        if (directError instanceof Error && 'code' in directError) {
          console.error('SQL Error Code:', (directError as any).code);
          console.error('SQL Error Detail:', (directError as any).detail);
          console.error('SQL Error Position:', (directError as any).position);
          console.error('SQL Error Routine:', (directError as any).routine);
        }
        return {
          isSuccess: false,
          error: directError instanceof Error ? directError.message : 'Unknown error'
        };
      }
      
      const newLastId = events[events.length - 1].sortableUniqueId;
      
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
    const partitionInfo = this.extractPartitionInfo();
    
    // For now, use a simpler approach to get delta events
    // In production, would use proper EventRetrievalInfo
    const allEvents = await this.getAllEventsAsync();
    const fromIndex = allEvents.findIndex(e => e.sortableUniqueId === fromSortableUniqueId);
    if (fromIndex === -1) {
      return allEvents.slice(0, limit);
    }
    return allEvents.slice(fromIndex + 1, fromIndex + 1 + limit);
    
    // Handled above
  }
  
  /**
   * Get all events
   */
  async getAllEventsAsync(): Promise<SerializableEventDocument[]> {
    const partitionInfo = this.extractPartitionInfo();
    
    // Query events directly from PostgreSQL
    try {
      const query = `
        SELECT 
          id,
          payload,
          sortable_unique_id,
          version,
          aggregate_id,
          root_partition_key,
          timestamp,
          partition_key,
          aggregate_group,
          payload_type_name,
          causation_id,
          correlation_id,
          executed_user
        FROM events 
        WHERE aggregate_id = $1 
        AND root_partition_key = $2
        ORDER BY version ASC
        LIMIT 1000
      `;
      
      const result = await pool.query(query, [
        partitionInfo.aggregateId,
        partitionInfo.rootPartitionKey
      ]);
      
      return result.rows.map(row => this.fromDbEvent(row));
    } catch (error) {
      console.error('Failed to get all events:', error);
      return [];
    }
  }
  
  /**
   * Get last event ID
   */
  async getLastSortableUniqueIdAsync(): Promise<string> {
    return this.getLastSortableUniqueIdFromDb();
  }
  
  private async getLastSortableUniqueIdFromDb(): Promise<string> {
    const partitionInfo = this.extractPartitionInfo();
    
    try {
      const query = `
        SELECT sortable_unique_id 
        FROM events 
        WHERE aggregate_id = $1 
        AND root_partition_key = $2
        ORDER BY version DESC 
        LIMIT 1
      `;
      
      const result = await pool.query(query, [
        partitionInfo.aggregateId,
        partitionInfo.rootPartitionKey
      ]);
      
      return result.rows[0]?.sortable_unique_id || '';
    } catch (error) {
      console.error('Failed to get last sortable unique ID:', error);
      return '';
    }
  }
  
  private extractPartitionInfo() {
    // Actor ID format: "rootPartition@aggregateId@aggregateType"
    const parts = this.actorId.split('@');
    return {
      rootPartitionKey: parts[0] || 'default',
      aggregateId: parts[1] || crypto.randomUUID(),
      aggregateType: parts[2] || 'Task'
    };
  }
  
  private toDbEvent(event: SerializableEventDocument): any {
    const partitionKey = event.partitionKeys.partitionKey || 
      `${event.partitionKeys.rootPartitionKey || 'default'}@${event.aggregateId}@${event.partitionKeys.group || 'Task'}`;
    
    return {
      id: event.id,
      payload: event.payload,  // Changed from eventData to payload
      eventData: event.payload, // Keep eventData for C# compatibility
      sortableUniqueId: event.sortableUniqueId,
      version: event.version,
      aggregateId: event.aggregateId,
      partitionKeys: event.partitionKeys,
      timestamp: new Date(event.createdAt),
      partitionKey: partitionKey,
      aggregateGroup: event.partitionKeys.group || 'Task',
      aggregateType: event.partitionKeys.group || 'Task',
      eventType: event.eventType,
      metadata: {
        causationId: event.metadata?.causationId || '',
        correlationId: event.metadata?.correlationId || '',
        executedUser: event.metadata?.userId || '',
        userId: event.metadata?.userId || '',
        timestamp: new Date(event.createdAt)
      }
    };
  }
  
  private fromDbEvent(dbEvent: any): SerializableEventDocument {
    return {
      id: dbEvent.id,
      sortableUniqueId: dbEvent.sortable_unique_id,
      payload: dbEvent.payload,
      eventType: dbEvent.payload_type_name,
      aggregateId: dbEvent.aggregate_id,
      partitionKeys: {
        aggregateId: dbEvent.aggregate_id,
        group: dbEvent.aggregate_group,
        rootPartitionKey: dbEvent.root_partition_key,
        partitionKey: dbEvent.partition_key
      },
      version: dbEvent.version,
      createdAt: dbEvent.timestamp.toISOString(),
      metadata: {
        causationId: dbEvent.causation_id,
        correlationId: dbEvent.correlation_id,
        userId: dbEvent.executed_user
      }
    };
  }
}

/**
 * Aggregate Actor - manages aggregate state and command processing
 * This follows the C# IAggregateActor pattern
 */
export class AggregateActor {
  private readonly eventHandler: AggregateEventHandlerActor;
  
  constructor(
    private readonly actorId: string
  ) {
    this.eventHandler = new AggregateEventHandlerActor(actorId);
  }
  
  /**
   * Initialize database tables
   */
  async initialize(): Promise<void> {
    // Skip initialization since table is already created
    console.log('Skipping table initialization - using existing table');
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
        const sortableUniqueId = `${new Date().toISOString()}-${crypto.randomUUID()}`;
        const event: SerializableEventDocument = {
          id: eventId,
          sortableUniqueId,
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
    // Format: "rootPartition@aggregateId@aggregateType"
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
export async function createPostgresActor(actorId: string): Promise<AggregateActor> {
  const actor = new AggregateActor(actorId);
  await actor.initialize();
  return actor;
}