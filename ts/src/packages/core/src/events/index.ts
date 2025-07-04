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

// Event store interfaces are exported from storage module
// to avoid conflicts

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

/**
 * Export event reader and writer interfaces
 */
export type { IEventReader } from './event-reader.js';
export type { IEventWriter } from './event-writer.js';

/**
 * Export event retrieval info and related types
 */
export {
  EventRetrievalInfo,
  OptionalValue,
  SortableIdCondition,
  SortableIdConditionNone,
  SinceSortableIdCondition,
  BetweenSortableIdCondition,
  AggregateGroupStream
} from './event-retrieval-info.js';

export type {
  ISortableIdCondition,
  IAggregatesStream
} from './event-retrieval-info.js';