// Core interfaces and types
export {
  AggregateProjector,
  type IAggregateProjector,
  type ITypedAggregatePayload
} from './aggregates/aggregate-projector.js';

export * from './commands/command.js';
export * from './events/event-payload.js';

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

// Re-export commonly used utilities
export { ok, err, type Result } from 'neverthrow';