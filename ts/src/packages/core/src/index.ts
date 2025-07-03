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
export type { ValidationResult, Validator, ZodSchema } from './validation'
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
export type { 
  IEventPayload,
  IEvent,
  CreateEventOptions,
  CreateEventMetadataOptions,
  IEventReader,
  IEventWriter
} from './events'

export { 
  isEventPayload,
  Event,
  EventMetadata,
  createEvent,
  createEventMetadata,
  EventDocument,
  SerializableEventDocument,
  toSerializableEventDocument,
  fromSerializableEventDocument,
  InMemoryEventStore,
  InMemoryEventReader,
  InMemoryEventWriter
} from './events'

/**
 * Export aggregate types
 */
export type { 
  IAggregatePayload,
  IAggregate,
  IAggregateProjector,
  IProjector
} from './aggregates'

export {
  isAggregatePayload,
  Aggregate,
  EmptyAggregatePayload,
  createEmptyAggregate,
  isEmptyAggregate,
  ProjectionResult,
  EventOrNone,
  createProjector,
  AggregateProjector
} from './aggregates'

/**
 * Export command types
 */
export type {
  ICommand,
  ICommandHandler,
  ICommandContext,
  ICommandContextWithoutState,
  ICommandWithHandler,
  CommandResponse,
  ValidationRule,
  CommandValidator,
  ValidationRules
} from './commands'

export {
  createCommandResponse,
  createCommandValidator,
  validateCommand,
  required,
  minLength,
  maxLength,
  email,
  range,
  pattern,
  custom
} from './commands'

/**
 * Export query types
 */
export type {
  IQuery,
  IQueryContext,
  IMultiProjectionQuery,
  IMultiProjectionListQuery,
  IQueryPagingParameter,
  QueryResult,
  ListQueryResult,
  IMultiProjector
} from './queries'

export {
  createQueryResult,
  createListQueryResult,
  MultiProjectionState,
  AggregateListProjector,
  AggregateListPayload,
  createAggregateListProjector
} from './queries'

/**
 * Export executor types
 */
export type {
  ISekibanExecutor,
  ICommandExecutor,
  IQueryExecutor,
  CommandResponse,
  QueryResponse,
  ExecutorConfig
} from './executors/sekiban-executor'

export {
  InMemorySekibanExecutor
} from './executors/sekiban-executor'

/**
 * Export storage provider types
 */
export type {
  IEventStorageProvider,
  StorageProviderConfig,
  EventBatch,
  SnapshotData
} from './storage'

export {
  StorageProviderType
} from './storage'

export {
  StorageError,
  ConnectionError,
  ConcurrencyError as StorageConcurrencyError,
  StorageProviderFactory,
  InMemoryStorageProvider,
  CosmosStorageProvider,
  PostgresStorageProvider
} from './storage'

/**
 * Version information
 */
export const VERSION = '0.0.1';