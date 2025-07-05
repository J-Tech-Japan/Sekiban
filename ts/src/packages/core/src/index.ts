// Core interfaces and types
export {
  AggregateProjector,
  type IAggregateProjector,
  type ITypedAggregatePayload
} from './aggregates/aggregate-projector.js';

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

// Domain Types Registry
export * from './domain-types/index.js';

// Re-export commonly used utilities
export { ok, err, type Result } from 'neverthrow';