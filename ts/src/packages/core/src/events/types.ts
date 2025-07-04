import { PartitionKeys, SortableUniqueId, Metadata, MetadataBuilder } from '../documents/index.js';
import { IEventPayload } from './event-payload.js';

/**
 * Represents a domain event with its metadata
 */
export interface Event<TPayload extends IEventPayload = IEventPayload> {
  /**
   * The unique identifier for this event
   */
  id: SortableUniqueId;
  
  /**
   * The partition keys for the aggregate this event belongs to
   */
  partitionKeys: PartitionKeys;
  
  /**
   * The aggregate type this event belongs to
   */
  aggregateType: string;
  
  /**
   * The type of this event
   */
  eventType: string;
  
  /**
   * The version of the aggregate after this event
   */
  version: number;
  
  /**
   * The event payload
   */
  payload: TPayload;
  
  /**
   * Event metadata
   */
  metadata: Metadata;
}

/**
 * Event filter criteria
 */
export interface EventFilter {
  /**
   * Filter by aggregate ID
   */
  aggregateId?: string;
  
  /**
   * Filter by aggregate type
   */
  aggregateType?: string;
  
  /**
   * Filter by event types
   */
  eventTypes?: string[];
  
  /**
   * Filter by minimum version
   */
  fromVersion?: number;
  
  /**
   * Filter by maximum version
   */
  toVersion?: number;
  
  /**
   * Filter by partition keys
   */
  partitionKeys?: PartitionKeys;
  
  /**
   * Filter by time range - from
   */
  fromTimestamp?: Date;
  
  /**
   * Filter by time range - to
   */
  toTimestamp?: Date;
}

/**
 * Event stream subscription
 */
export interface EventSubscription {
  /**
   * Unique identifier for the subscription
   */
  id: string;
  
  /**
   * Unsubscribe from the event stream
   */
  unsubscribe(): Promise<void>;
}

/**
 * Event handler function
 */
export type EventHandler<TPayload extends IEventPayload = IEventPayload> = (
  event: Event<TPayload>
) => Promise<void>;

/**
 * Builder for creating events
 */
export class EventBuilder<TPayload extends IEventPayload> {
  private event: Partial<Event<TPayload>> = {};

  /**
   * Sets the event ID
   */
  withId(id: SortableUniqueId): EventBuilder<TPayload> {
    this.event.id = id;
    return this;
  }

  /**
   * Sets the partition keys
   */
  withPartitionKeys(partitionKeys: PartitionKeys): EventBuilder<TPayload> {
    this.event.partitionKeys = partitionKeys;
    return this;
  }

  /**
   * Sets the aggregate type
   */
  withAggregateType(aggregateType: string): EventBuilder<TPayload> {
    this.event.aggregateType = aggregateType;
    return this;
  }

  /**
   * Sets the event type
   */
  withEventType(eventType: string): EventBuilder<TPayload> {
    this.event.eventType = eventType;
    return this;
  }

  /**
   * Sets the version
   */
  withVersion(version: number): EventBuilder<TPayload> {
    this.event.version = version;
    return this;
  }

  /**
   * Sets the payload
   */
  withPayload(payload: TPayload): EventBuilder<TPayload> {
    this.event.payload = payload;
    return this;
  }

  /**
   * Sets the metadata
   */
  withMetadata(metadata: Metadata): EventBuilder<TPayload> {
    this.event.metadata = metadata;
    return this;
  }

  /**
   * Builds the event
   */
  build(): Event<TPayload> {
    if (!this.event.id) {
      this.event.id = SortableUniqueId.generate();
    }
    if (!this.event.metadata) {
      this.event.metadata = new MetadataBuilder().build();
    }
    
    const { id, partitionKeys, aggregateType, eventType, version, payload, metadata } = this.event;
    
    if (!partitionKeys || !aggregateType || !eventType || version === undefined || !payload) {
      throw new Error('Missing required event properties');
    }
    
    return {
      id,
      partitionKeys,
      aggregateType,
      eventType,
      version,
      payload,
      metadata,
    } as Event<TPayload>;
  }
}