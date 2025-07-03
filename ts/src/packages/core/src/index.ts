/**
 * @sekiban/core - Event Sourcing and CQRS framework for TypeScript
 */

/**
 * Re-export Result types from neverthrow
 */
export { Result, Ok, Err, ok, err, ResultAsync, okAsync, errAsync } from 'neverthrow';

/**
 * Export error types
 */
export { 
  SekibanError,
  ValidationError,
  AggregateNotFoundError,
  CommandValidationError,
  EventApplicationError,
  QueryExecutionError,
  SerializationError,
  EventStoreError,
  ConcurrencyError,
  UnsupportedOperationError
} from './result/errors';

/**
 * Export date producer utilities
 */
export type { ISekibanDateProducer } from './date-producer'
export { SekibanDateProducer, createMockDateProducer, createSequentialDateProducer } from './date-producer'

/**
 * Export UUID utilities
 */
export { 
  generateUuid, 
  createVersion7, 
  isValidUuid, 
  createNamespacedUuid,
  createDeterministicUuid 
} from './utils/uuid'

/**
 * Export validation utilities
 */
export type { ValidationError, ValidationResult, Validator, ZodSchema } from './validation'
export { 
  createValidator,
  isValid,
  getErrors,
  validateOrThrow
} from './validation'

/**
 * Export document types
 */
export { 
  SortableUniqueId,
  SortableUniqueIdUtils,
  type ISortableUniqueId 
} from './documents/sortable-unique-id'

export { 
  PartitionKeys,
  PartitionKeysBuilder,
  PartitionKeysUtils,
  type IPartitionKeys 
} from './documents/partition-keys'

export { 
  Metadata,
  MetadataBuilder,
  type Metadata as IMetadata 
} from './documents/metadata'

/**
 * Export event types
 */
export { 
  IEventPayload,
  isEventPayload,
  IEvent,
  Event,
  EventMetadata,
  createEvent,
  createEventMetadata,
  type CreateEventOptions,
  type CreateEventMetadataOptions,
  EventDocument,
  SerializableEventDocument,
  toSerializableEventDocument,
  fromSerializableEventDocument,
  InMemoryEventStore,
  InMemoryEventReader,
  InMemoryEventWriter,
  IEventReader,
  IEventWriter
} from './events'

/**
 * Export aggregate types
 */
export { 
  IAggregatePayload,
  isAggregatePayload
} from './aggregates'

/**
 * Version information
 */
export const VERSION = '0.0.1';