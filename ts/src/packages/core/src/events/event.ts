import { IEventPayload } from './event-payload'
import { SortableUniqueId } from '../documents/sortable-unique-id'
import { PartitionKeys } from '../documents/partition-keys'
import { Metadata, MetadataBuilder } from '../documents/metadata'

/**
 * Metadata specific to events, extending the base Metadata type
 */
export interface EventMetadata extends Metadata {
  /**
   * Correlation ID for tracking related operations
   */
  correlationId?: string
  
  /**
   * Causation ID linking cause and effect
   */
  causationId?: string
  
  /**
   * User ID who initiated the event
   */
  userId?: string
  
  /**
   * Custom metadata specific to the event
   */
  custom?: Record<string, unknown>
}

/**
 * Represents an event in the event sourcing system
 */
export interface IEvent<TPayload extends IEventPayload = IEventPayload> {
  /**
   * Unique identifier for the event
   */
  id: SortableUniqueId
  
  /**
   * Partition keys for the aggregate
   */
  partitionKeys: PartitionKeys
  
  /**
   * Type of the aggregate this event belongs to
   */
  aggregateType: string
  
  /**
   * Version number of the aggregate after this event
   */
  version: number
  
  /**
   * The event payload containing domain data
   */
  payload: TPayload
  
  /**
   * Metadata about the event
   */
  metadata: EventMetadata
}

/**
 * Concrete implementation of IEvent
 */
export class Event<TPayload extends IEventPayload = IEventPayload> implements IEvent<TPayload> {
  constructor(
    public readonly id: SortableUniqueId,
    public readonly partitionKeys: PartitionKeys,
    public readonly aggregateType: string,
    public readonly version: number,
    public readonly payload: TPayload,
    public readonly metadata: EventMetadata
  ) {
    // Freeze the event to ensure immutability
    Object.freeze(this)
    Object.freeze(this.payload)
    Object.freeze(this.metadata)
  }
}

/**
 * Options for creating event metadata
 */
export interface CreateEventMetadataOptions {
  correlationId?: string
  causationId?: string
  userId?: string
  timestamp?: Date
  custom?: Record<string, unknown>
}

/**
 * Creates event metadata with the provided options
 */
export function createEventMetadata(options?: CreateEventMetadataOptions): EventMetadata {
  const builder = new MetadataBuilder()
  
  if (options?.timestamp) {
    builder.withTimestamp(options.timestamp)
  }
  
  if (options?.userId) {
    builder.withUserId(options.userId)
  }
  
  if (options?.correlationId) {
    builder.withCorrelationId(options.correlationId)
  }
  
  if (options?.causationId) {
    builder.withCausationId(options.causationId)
  }
  
  if (options?.custom) {
    builder.withCustomData(options.custom)
  }
  
  return builder.build() as EventMetadata
}

/**
 * Options for creating an event
 */
export interface CreateEventOptions<TPayload extends IEventPayload> {
  /**
   * Optional event ID. If not provided, a new one will be generated
   */
  id?: SortableUniqueId
  
  /**
   * Partition keys for the aggregate
   */
  partitionKeys: PartitionKeys
  
  /**
   * Type of the aggregate
   */
  aggregateType: string
  
  /**
   * Version number of the aggregate
   */
  version: number
  
  /**
   * Event payload
   */
  payload: TPayload
  
  /**
   * Optional metadata. If not provided, default metadata will be created
   */
  metadata?: EventMetadata
}

/**
 * Helper function to create an event
 */
export function createEvent<TPayload extends IEventPayload>(
  options: CreateEventOptions<TPayload>
): Event<TPayload> {
  return new Event(
    options.id ?? SortableUniqueId.generate(),
    options.partitionKeys,
    options.aggregateType,
    options.version,
    options.payload,
    options.metadata ?? createEventMetadata()
  )
}