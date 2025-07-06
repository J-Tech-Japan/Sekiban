import type { 
  Aggregate,
  PartitionKeys,
  EventDocument,
  CommandExecutionResult,
  IEventPayload,
  IAggregatePayload,
  ICommandWithHandler,
  IAggregateProjector,
  ITypedAggregatePayload,
  EmptyAggregatePayload,
  Metadata
} from '@sekiban/core';

/**
 * Serializable representation of an aggregate
 */
export interface SerializableAggregate {
  partitionKeys: PartitionKeys;
  aggregate: Aggregate;
  lastSortableUniqueId: string;
}

/**
 * Actor-specific serializable command with metadata
 * Updated to work with ICommandWithHandler pattern
 */
export interface ActorSerializableCommandAndMetadata<
  TCommand = any,
  TProjector extends IAggregateProjector<TPayloadUnion> = any,
  TPayloadUnion extends ITypedAggregatePayload = any,
  TAggregatePayload extends TPayloadUnion | EmptyAggregatePayload = TPayloadUnion | EmptyAggregatePayload
> {
  command: ICommandWithHandler<TCommand, TProjector, TPayloadUnion, TAggregatePayload>;
  commandData: TCommand;
  partitionKeys: PartitionKeys;
  metadata?: Metadata;
}

/**
 * Serializable event document
 */
export interface SerializableEventDocument {
  id: string;
  sortableUniqueId: string;
  payload: any; // JSON object
  eventType: string;
  aggregateId: string;
  partitionKeys: PartitionKeys;
  version: number;
  createdAt: string; // ISO date string
  metadata: any;
}

/**
 * Event handling response
 */
export interface EventHandlingResponse {
  isSuccess: boolean;
  error?: string;
  lastSortableUniqueId?: string;
}

/**
 * Serializable query
 */
export interface SerializableQuery {
  queryType: string;
  payload: any;
  partitionKeys?: PartitionKeys;
}

/**
 * Serializable list query
 */
export interface SerializableListQuery extends SerializableQuery {
  skip?: number;
  limit?: number;
  sortField?: string;
  sortDirection?: 'asc' | 'desc';
}

/**
 * Query response
 */
export interface QueryResponse {
  isSuccess: boolean;
  data?: any;
  error?: string;
}

/**
 * List query response
 */
export interface ListQueryResponse extends QueryResponse {
  totalCount?: number;
  items?: any[];
}

/**
 * Multi-projection state
 */
export interface MultiProjectionState {
  projections: Record<string, any>;
  lastProcessedEventId: string;
  lastProcessedTimestamp: string;
  version: number;
}

/**
 * Dapr event envelope for PubSub
 */
export interface DaprEventEnvelope {
  event: SerializableEventDocument;
  topic: string;
  pubsubName: string;
}

/**
 * Aggregate actor interface - manages aggregate state
 */
export interface IAggregateActor {
  /**
   * Get current aggregate state
   */
  getAggregateStateAsync(): Promise<SerializableAggregate>;
  
  /**
   * Execute command and return response as JSON string
   */
  executeCommandAsync(command: ActorSerializableCommandAndMetadata): Promise<string>;
  
  /**
   * Rebuild state from all events
   */
  rebuildStateAsync(): Promise<void>;
  
  /**
   * Timer callback for periodic saving
   */
  saveStateCallbackAsync(state?: any): Promise<void>;
  
  /**
   * Reminder handling
   */
  receiveReminderAsync(
    reminderName: string,
    state: Buffer,
    dueTime: string, // ISO duration
    period: string   // ISO duration
  ): Promise<void>;
}

/**
 * Event handler actor interface - manages event streams
 */
export interface IAggregateEventHandlerActor {
  /**
   * Append events with concurrency check
   */
  appendEventsAsync(
    expectedLastSortableUniqueId: string,
    events: SerializableEventDocument[]
  ): Promise<EventHandlingResponse>;
  
  /**
   * Get events after a specific point
   */
  getDeltaEventsAsync(
    fromSortableUniqueId: string,
    limit: number
  ): Promise<SerializableEventDocument[]>;
  
  /**
   * Get all events
   */
  getAllEventsAsync(): Promise<SerializableEventDocument[]>;
  
  /**
   * Get last event ID
   */
  getLastSortableUniqueIdAsync(): Promise<string>;
  
  /**
   * Register projector (currently no-op)
   */
  registerProjectorAsync(projectorKey: string): Promise<void>;
}

/**
 * Multi-projector actor interface - handles cross-aggregate projections
 */
export interface IMultiProjectorActor {
  /**
   * Execute single-item query
   */
  queryAsync(query: SerializableQuery): Promise<QueryResponse>;
  
  /**
   * Execute list query
   */
  queryListAsync(query: SerializableListQuery): Promise<ListQueryResponse>;
  
  /**
   * Check if event has been processed
   */
  isSortableUniqueIdReceived(sortableUniqueId: string): Promise<boolean>;
  
  /**
   * Build current state from buffer
   */
  buildStateAsync(): Promise<MultiProjectionState>;
  
  /**
   * Rebuild state from scratch
   */
  rebuildStateAsync(): Promise<void>;
  
  /**
   * Handle published event
   */
  handlePublishedEvent(envelope: DaprEventEnvelope): Promise<void>;
}

/**
 * Actor partition info stored in state
 */
export interface ActorPartitionInfo {
  partitionKeys: PartitionKeys;
  aggregateType: string;
  projectorType: string;
}

/**
 * Aggregate event handler state
 */
export interface AggregateEventHandlerState {
  lastSortableUniqueId: string;
  eventCount: number;
}

/**
 * Buffered event for multi-projector
 */
export interface BufferedEvent {
  event: SerializableEventDocument;
  receivedAt: Date;
}