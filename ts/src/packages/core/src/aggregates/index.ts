/**
 * Export aggregate payload types
 */
export type { IAggregatePayload } from './aggregate-payload'
export { isAggregatePayload } from './aggregate-payload'

/**
 * Export types from types.ts
 */
export type { 
  IAggregateFactory,
  IAggregateLoader,
  IProjector as IBaseProjector
} from './types'
export { AggregateFactory } from './types'

/**
 * Re-export IAggregateLoader from loader module for backwards compatibility
 */
export type { IAggregateLoader as IAggregateLoaderFromLoader } from './types'

/**
 * Export aggregate types
 */
export type { IAggregate } from './aggregate'
export { 
  Aggregate,
  EmptyAggregatePayload,
  createEmptyAggregate,
  isEmptyAggregate
} from './aggregate'

/**
 * Export projector interfaces
 */
export type { 
  IAggregateProjector,
  IProjector
} from './projector-interface'

export {
  ProjectionResult,
  EventOrNone,
  createProjector
} from './projector-interface'

/**
 * Export aggregate projector
 */
export { AggregateProjector } from './aggregate-projector'

/**
 * Export typed aggregate payload types
 */
export type {
  ITypedAggregatePayload,
  EmptyAggregatePayload as IEmptyAggregatePayload,
  IAggregateProjector as ITypedAggregateProjector
} from './aggregate-projector'