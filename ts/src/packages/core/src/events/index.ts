/**
 * Export event payload interfaces
 */
export type { IEventPayload } from './event-payload.js';
export { isEventPayload } from './event-payload.js';

/**
 * Export event interfaces and classes
 */
export type { 
  IEvent,
  EventMetadata,
  CreateEventMetadataOptions,
  CreateEventOptions
} from './event.js';

export {
  Event,
  createEventMetadata,
  createEvent
} from './event.js';

/**
 * Export event document types
 */
export {
  EventDocument
} from './event-document.js';

/**
 * Export event store interfaces
 */
export type {
  IEventStore,
  EventStoreOptions,
  EventStoreStats
} from './store.js';

/**
 * Export in-memory event store
 */
export { InMemoryEventStore } from './in-memory-event-store.js';

/**
 * Export event stream types
 */
export type {
  IEventStream
} from './stream.js';

export {
  InMemoryEventStream
} from './stream.js';

/**
 * Export event types including Event and EventFilter
 */
export type {
  Event as IEventType,
  EventFilter
} from './types.js';

export { EventBuilder } from './types.js';