// Core interfaces and types
export {
  AggregateProjector,
  type IAggregateProjector,
  type ITypedAggregatePayload
} from './aggregates/aggregate-projector';

// Export IProjector and related types
export {
  type IProjector,
  type IAggregateProjector as IBaseAggregateProjector,
  ProjectionResult,
  EventOrNone,
  createProjector
} from './aggregates/projector-interface';

export * from './events/event-payload';
export * from './events/index';
export { type IEventPayload } from './events/event-payload';
export { type IEvent } from './events/event';
export { 
  SekibanError,
  CommandValidationError,
  AggregateNotFoundError
} from './result/errors';
export { InMemoryEventStore } from './storage/in-memory-event-store';
export { StorageProviderType, type IEventStore } from './storage/storage-provider';

export {
  Aggregate,
  EmptyAggregatePayload,
  createEmptyAggregate,
  isEmptyAggregate,
  type IAggregate
} from './aggregates/aggregate';

// Export IAggregatePayload
export type { IAggregatePayload } from './aggregates/aggregate-payload';

export * from './documents/partition-keys';
export { PartitionKeys } from './documents/partition-keys';
export { SortableUniqueId } from './documents/sortable-unique-id';
export * from './result/errors';
export * from './storage/index';

// Schema Registry (modern schema-first approach)
export * from './schema-registry/index';

// Projectors
export * from './projectors/index';

// Explicitly export defineProjector and other schema functions
export { 
  defineEvent,
  defineCommand,
  defineProjector,
  command,
  SchemaRegistry,
  SchemaExecutor,
  createSchemaDomainTypes,
  createSekibanDomainTypesFromGlobalRegistry,
  globalRegistry,
  registerEvent,
  registerCommand,
  registerProjector
} from './schema-registry/index';

// Re-export ICommandContext types for convenience
export type {
  ICommandContext,
  ICommandContextWithoutState,
  ICommandWithHandler
} from './schema-registry/index';

// Domain Types Registry - selectively export to avoid conflicts
export { 
  type SekibanDomainTypes,
  type IEventTypes,
  type ICommandTypes,
  type IProjectorTypes,
  type IQueryTypes,
  type IAggregateTypes,
  type ISekibanSerializer
} from './domain-types/index';

// Re-export commonly used utilities
export { ok, err, Result } from 'neverthrow';

// Export validation utilities from commands
export {
  validateCommand,
  required,
  minLength,
  maxLength,
  email,
  range,
  pattern,
  custom,
  createCommandValidator,
  type ValidationRule,
  type CommandValidator,
  type ValidationRules
} from './commands/index';

// Export command executor
export {
  UnifiedCommandExecutor,
  createUnifiedExecutor,
  type CommandExecutionResult,
  type UnifiedCommandExecutionOptions,
  type IServiceProvider
} from './commands/index';

// Export ICommand interface
export { type ICommand } from './commands/command';

// Export utilities
export {
  generateUuid,
  createVersion7,
  isValidUuid
} from './utils/index';

// Export Metadata type
export { Metadata } from './documents/metadata';

// Export query types
export * from './queries/index';

// Export event retrieval info and related types
export {
  EventRetrievalInfo,
  OptionalValue,
  SortableIdCondition,
  AggregateGroupStream
} from './events/event-retrieval-info';

// Export EventDocument type
export type { EventDocument } from './domain-types/interfaces';

// Type aliases for backward compatibility
export type EventId = string;
export type AggregateId = string;
export type IListQueryResult<T> = import('./queries/query').ListQueryResult<T>;

// Re-export types that might be missing
export { ListQueryResult } from './queries/query';
export { MultiProjectionState } from './projectors/multi-projector-types';
export { IMultiProjectorCommon, IMultiProjectorStateCommon } from './projectors/index';
export { AggregateListProjector } from './queries/aggregate-list-projector';
export { IMultiProjectionListQuery, IQueryContext } from './queries/index';
export { SchemaMultiProjectorTypes, SchemaQueryTypes } from './schema-registry/index';

// Version
export const VERSION = '0.0.1';