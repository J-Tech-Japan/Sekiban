/**
 * Export event payload interfaces
 */
export type { IEventPayload } from './event-payload';
export { isEventPayload } from './event-payload';

/**
 * Export event interfaces and classes
 */
export type { 
  IEvent,
  EventMetadata,
  CreateEventMetadataOptions,
  CreateEventOptions
} from './event';

/**
 * Re-export IEvent from event.js for backwards compatibility
 */
export type { IEvent as IEventFromEventJs } from './event';

export {
  Event,
  createEventMetadata,
  createEvent
} from './event';

/**
 * Export event document types
 */
export {
  EventDocument
} from './event-document';

// Event store interfaces are exported from storage module
// to avoid conflicts

/**
 * Export event stream types
 */
export type {
  IEventStream
} from './stream';

export {
  InMemoryEventStream
} from './stream';

/**
 * Export event types including Event and EventFilter
 */
export type {
  Event as IEventType,
  EventFilter
} from './types';

export { EventBuilder } from './types';

/**
 * Export event reader and writer interfaces
 */
export type { IEventReader } from './event-reader';
export type { IEventWriter } from './event-writer';

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
} from './event-retrieval-info';

export type {
  ISortableIdCondition,
  IAggregatesStream
} from './event-retrieval-info';