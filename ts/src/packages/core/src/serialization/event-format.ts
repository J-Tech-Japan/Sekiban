import { Event, IEventPayload } from '../events/index.js';
import { IJsonSerializer, defaultJsonSerializer } from './json';
import { Result, ok, err } from 'neverthrow';
import { SerializationError } from '../result/index.js';
import { SortableUniqueId, SortableUniqueIdUtils, PartitionKeys, Metadata } from '../documents/index.js';

/**
 * Serialized event format for storage
 */
export interface SerializedEvent {
  /**
   * Event ID as string
   */
  id: string;
  
  /**
   * Aggregate ID
   */
  aggregateId: string;
  
  /**
   * Aggregate type
   */
  aggregateType: string;
  
  /**
   * Event version
   */
  version: number;
  
  /**
   * Event type
   */
  eventType: string;
  
  /**
   * Serialized event payload
   */
  payload: string;
  
  /**
   * Event metadata as JSON
   */
  metadata: string;
  
  /**
   * Partition keys as JSON
   */
  partitionKeys: string;
  
  /**
   * Event timestamp
   */
  timestamp: string;
}

/**
 * Event serializer for converting events to/from storage format
 */
export class EventSerializer {
  constructor(
    private jsonSerializer: IJsonSerializer = defaultJsonSerializer
  ) {}

  /**
   * Serializes an event for storage
   */
  serialize(event: Event): Result<SerializedEvent, SerializationError> {
    const payloadResult = this.jsonSerializer.serialize(event.payload);
    if (payloadResult.isErr()) {
      return err(payloadResult.error);
    }

    const metadataResult = this.jsonSerializer.serialize(event.metadata);
    if (metadataResult.isErr()) {
      return err(metadataResult.error);
    }

    const partitionKeysResult = this.jsonSerializer.serialize(event.partitionKeys);
    if (partitionKeysResult.isErr()) {
      return err(partitionKeysResult.error);
    }

    return ok({
      id: event.id.toString(),
      aggregateId: event.partitionKeys.aggregateId,
      aggregateType: event.aggregateType,
      version: event.version,
      eventType: event.eventType,
      payload: payloadResult.value,
      metadata: metadataResult.value,
      partitionKeys: partitionKeysResult.value,
      timestamp: event.metadata.timestamp.toISOString(),
    });
  }

  /**
   * Deserializes an event from storage format
   */
  deserialize<TPayload extends IEventPayload = IEventPayload>(
    serialized: SerializedEvent,
    payloadType?: new(...args: any[]) => TPayload
  ): Result<Event<TPayload>, SerializationError> {
    const payloadResult = this.jsonSerializer.deserialize<TPayload>(
      serialized.payload,
      payloadType
    );
    if (payloadResult.isErr()) {
      return err(payloadResult.error);
    }

    const metadataResult = this.jsonSerializer.deserialize(serialized.metadata);
    if (metadataResult.isErr()) {
      return err(metadataResult.error);
    }

    const partitionKeysResult = this.jsonSerializer.deserialize(serialized.partitionKeys);
    if (partitionKeysResult.isErr()) {
      return err(partitionKeysResult.error);
    }

    // Parse the event ID
    const idParts = serialized.id.split('-');
    if (idParts.length < 2) {
      return err(new SerializationError('deserialize', 'Invalid event ID format'));
    }

    const timestamp = parseInt(idParts[0]!, 10);
    const uniqueId = idParts.slice(1).join('-');

    const idResult = SortableUniqueId.fromString(serialized.id);
    if (idResult.isErr()) {
      return err(new SerializationError('deserialize', 'Invalid event ID format'));
    }

    return ok({
      id: idResult.value,
      partitionKeys: partitionKeysResult.value as PartitionKeys,
      aggregateType: serialized.aggregateType,
      eventType: serialized.eventType,
      version: serialized.version,
      payload: payloadResult.value,
      metadata: metadataResult.value as Metadata,
    } as Event<TPayload>);
  }

  /**
   * Serializes multiple events
   */
  serializeMany(events: Event[]): Result<SerializedEvent[], SerializationError> {
    const results: SerializedEvent[] = [];
    
    for (const event of events) {
      const result = this.serialize(event);
      if (result.isErr()) {
        return err(result.error);
      }
      results.push(result.value);
    }
    
    return ok(results);
  }

  /**
   * Deserializes multiple events
   */
  deserializeMany<TPayload extends IEventPayload = IEventPayload>(
    serialized: SerializedEvent[],
    payloadTypes?: Map<string, new(...args: any[]) => TPayload>
  ): Result<Event<TPayload>[], SerializationError> {
    const results: Event<TPayload>[] = [];
    
    for (const item of serialized) {
      const payloadType = payloadTypes?.get(item.eventType);
      const result = this.deserialize(item, payloadType);
      if (result.isErr()) {
        return err(result.error);
      }
      results.push(result.value);
    }
    
    return ok(results);
  }
}

/**
 * Event type registry for deserialization
 */
export class EventTypeRegistry {
  private types = new Map<string, new(...args: any[]) => IEventPayload>();

  /**
   * Registers an event type
   */
  register<T extends IEventPayload>(
    eventType: string,
    type: new(...args: any[]) => T
  ): void {
    this.types.set(eventType, type);
  }

  /**
   * Gets a registered type
   */
  get(eventType: string): (new(...args: any[]) => IEventPayload) | undefined {
    return this.types.get(eventType);
  }

  /**
   * Checks if a type is registered
   */
  has(eventType: string): boolean {
    return this.types.has(eventType);
  }

  /**
   * Gets all registered event types
   */
  getEventTypes(): string[] {
    return Array.from(this.types.keys());
  }

  /**
   * Clears all registrations
   */
  clear(): void {
    this.types.clear();
  }
}

/**
 * Global event serializer instance
 */
export const defaultEventSerializer = new EventSerializer();

/**
 * Global event type registry
 */
export const globalEventTypeRegistry = new EventTypeRegistry();