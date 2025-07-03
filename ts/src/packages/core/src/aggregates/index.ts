/**
 * Export aggregate payload types
 */
export type { IAggregatePayload } from './aggregate-payload'
export { isAggregatePayload } from './aggregate-payload'

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