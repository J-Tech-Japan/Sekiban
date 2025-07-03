import { Result, ok, err } from 'neverthrow'
import { IEvent, Event, createEventMetadata } from './event'
import { IEventPayload } from './event-payload'
import { SortableUniqueId } from '../documents/sortable-unique-id'
import { PartitionKeys } from '../documents/partition-keys'
import { SerializationError, ValidationError } from '../result/errors'

/**
 * Document wrapper for events, providing convenient access to event properties
 */
export class EventDocument<TPayload extends IEventPayload = IEventPayload> {
  constructor(public readonly event: IEvent<TPayload>) {}
  
  /**
   * Event ID
   */
  get id(): SortableUniqueId {
    return this.event.id
  }
  
  /**
   * Partition keys
   */
  get partitionKeys(): PartitionKeys {
    return this.event.partitionKeys
  }
  
  /**
   * Aggregate ID from partition keys
   */
  get aggregateId(): string {
    return this.event.partitionKeys.aggregateId
  }
  
  /**
   * Aggregate type
   */
  get aggregateType(): string {
    return this.event.aggregateType
  }
  
  /**
   * Event version
   */
  get version(): number {
    return this.event.version
  }
  
  /**
   * Event payload
   */
  get payload(): TPayload {
    return this.event.payload
  }
  
  /**
   * Event metadata
   */
  get metadata(): typeof this.event.metadata {
    return this.event.metadata
  }
  
  /**
   * Event timestamp from metadata
   */
  get timestamp(): Date {
    return this.event.metadata.timestamp
  }
  
  /**
   * Sortable ID as string
   */
  get sortableId(): string {
    return this.event.id.toString()
  }
}

/**
 * Serializable representation of an event document
 */
export interface SerializableEventDocument {
  /**
   * Event ID as string
   */
  id: string
  
  /**
   * Aggregate ID
   */
  aggregateId: string
  
  /**
   * Aggregate type
   */
  aggregateType: string
  
  /**
   * Event version
   */
  version: number
  
  /**
   * Serialized payload (JSON string)
   */
  payload: string
  
  /**
   * Payload type name for deserialization hints
   */
  payloadTypeName: string
  
  /**
   * Event timestamp as ISO string
   */
  timestamp: string
  
  /**
   * Partition key string
   */
  partitionKey: string
  
  /**
   * Group from partition keys (optional)
   */
  group?: string
  
  /**
   * Root partition key for multi-tenancy (optional)
   */
  rootPartitionKey?: string
  
  /**
   * Serialized metadata (JSON string)
   */
  metadata: string
}

/**
 * Convert an EventDocument to its serializable representation
 */
export function toSerializableEventDocument<TPayload extends IEventPayload>(
  document: EventDocument<TPayload>
): SerializableEventDocument {
  const event = document.event
  
  // Get payload type name
  let payloadTypeName = 'Object'
  if (event.payload && typeof event.payload === 'object') {
    if (event.payload.constructor && event.payload.constructor.name !== 'Object') {
      payloadTypeName = event.payload.constructor.name
    }
  }
  
  return {
    id: event.id.toString(),
    aggregateId: event.partitionKeys.aggregateId,
    aggregateType: event.aggregateType,
    version: event.version,
    payload: JSON.stringify(event.payload),
    payloadTypeName,
    timestamp: event.metadata.timestamp.toISOString(),
    partitionKey: event.partitionKeys.toString(),
    group: event.partitionKeys.group,
    rootPartitionKey: event.partitionKeys.rootPartitionKey,
    metadata: JSON.stringify({
      userId: event.metadata.userId,
      correlationId: event.metadata.correlationId,
      causationId: event.metadata.causationId,
      custom: event.metadata.custom
    })
  }
}

/**
 * Reconstruct an EventDocument from its serializable representation
 */
export function fromSerializableEventDocument(
  serializable: SerializableEventDocument
): Result<EventDocument, SerializationError | ValidationError> {
  try {
    // Parse sortable unique ID
    const idResult = SortableUniqueId.fromString(serializable.id)
    if (idResult.isErr()) {
      return err(idResult.error)
    }
    
    // Parse payload
    let payload: IEventPayload
    try {
      payload = JSON.parse(serializable.payload)
    } catch (error) {
      return err(new SerializationError('deserialize', `Failed to parse event payload: ${error}`))
    }
    
    // Parse metadata
    let metadataData: any
    try {
      metadataData = JSON.parse(serializable.metadata)
    } catch (error) {
      return err(new SerializationError('deserialize', `Failed to parse event metadata: ${error}`))
    }
    
    // Reconstruct partition keys
    const partitionKeys = new PartitionKeys(
      serializable.aggregateId,
      serializable.group,
      serializable.rootPartitionKey
    )
    
    // Create metadata with parsed timestamp
    const metadata = createEventMetadata({
      timestamp: new Date(serializable.timestamp),
      userId: metadataData.userId,
      correlationId: metadataData.correlationId,
      causationId: metadataData.causationId,
      custom: metadataData.custom
    })
    
    // Reconstruct event
    const event = new Event(
      idResult.value,
      partitionKeys,
      serializable.aggregateType,
      serializable.version,
      payload,
      metadata
    )
    
    return ok(new EventDocument(event))
  } catch (error) {
    return err(new SerializationError('deserialize', `Failed to reconstruct event document: ${error}`))
  }
}