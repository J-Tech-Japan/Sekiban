// Core interfaces and types
export {
  AggregateProjector,
  type IAggregateProjector,
  type ITypedAggregatePayload
} from './aggregates/aggregate-projector.js';

// Export IProjector and related types
export {
  type IProjector,
  type IAggregateProjector as IBaseAggregateProjector,
  ProjectionResult,
  EventOrNone,
  createProjector
} from './aggregates/projector-interface.js';

export * from './commands/command.js';
export * from './events/event-payload.js';
export * from './events/index.js';

export {
  Aggregate,
  EmptyAggregatePayload,
  createEmptyAggregate,
  isEmptyAggregate,
  type IAggregate
} from './aggregates/aggregate.js';

export * from './documents/partition-keys.js';
export { SortableUniqueId } from './documents/sortable-unique-id.js';
export * from './result/errors.js';
export * from './storage/index.js';

// Schema Registry (modern schema-first approach)
export * from './schema-registry/index.js';

// Domain Types Registry - selectively export to avoid conflicts
export { 
  type SekibanDomainTypes,
  type IEventTypes,
  type ICommandTypes,
  type IProjectorTypes,
  type IQueryTypes,
  type IAggregateTypes,
  type ISekibanSerializer
} from './domain-types/index.js';

// Re-export commonly used utilities
export { ok, err, type Result } from 'neverthrow';