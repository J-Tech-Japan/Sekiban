/**
 * Export event payload types
 */
export type { IEventPayload } from './event-payload'
export { isEventPayload } from './event-payload'

/**
 * Export event types and utilities
 */
export type {
  IEvent,
  CreateEventOptions,
  CreateEventMetadataOptions
} from './event'

export {
  Event,
  EventMetadata,
  createEvent,
  createEventMetadata
} from './event'

/**
 * Export event document types
 */
export {
  EventDocument,
  SerializableEventDocument,
  toSerializableEventDocument,
  fromSerializableEventDocument
} from './event-document'

/**
 * Export in-memory event store
 */
export {
  InMemoryEventStore,
  InMemoryEventReader,
  InMemoryEventWriter,
  IEventReader,
  IEventWriter
} from './in-memory-event-store'