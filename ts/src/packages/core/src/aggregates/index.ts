/**
 * Export aggregate payload types
 */
export type { IAggregatePayload } from './aggregate-payload.js'
export { isAggregatePayload } from './aggregate-payload.js'

/**
 * Export types from types.ts
 */
export type { 
  IAggregateFactory,
  IAggregateLoader,
  IProjector as IBaseProjector
} from './types.js'
export { AggregateFactory } from './types.js'

/**
 * Re-export IAggregateLoader from loader module for backwards compatibility
 */
export type { IAggregateLoader as IAggregateLoaderFromLoader } from './types.js'

/**
 * Export aggregate types
 */
export type { IAggregate } from './aggregate.js'
export { 
  Aggregate,
  EmptyAggregatePayload,
  createEmptyAggregate,
  isEmptyAggregate
} from './aggregate.js'

/**
 * Export projector interfaces
 */
export type { 
  IAggregateProjector,
  IProjector
} from './projector-interface.js'

export {
  ProjectionResult,
  EventOrNone,
  createProjector
} from './projector-interface.js'

/**
 * Export aggregate projector
 */
export { AggregateProjector } from './aggregate-projector.js'

/**
 * Export typed aggregate payload types
 */
export type {
  ITypedAggregatePayload,
  EmptyAggregatePayload as IEmptyAggregatePayload,
  IAggregateProjector as ITypedAggregateProjector
} from './aggregate-projector.js'