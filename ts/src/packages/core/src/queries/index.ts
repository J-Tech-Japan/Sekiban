/**
 * Export query interfaces
 */
export type {
  IQuery,
  IQueryContext,
  IMultiProjectionQuery,
  IMultiProjectionListQuery,
  IQueryPagingParameter,
  QueryResult,
  ListQueryResult
} from './query.js'

export {
  createQueryResult,
  createListQueryResult
} from './query.js'

/**
 * Export multi-projection interfaces
 */
export type {
  IMultiProjector
} from './multi-projection.js'

export {
  MultiProjectionState
} from './multi-projection.js'

/**
 * Export aggregate list projector
 */
// TODO: Fix aggregate-list-projector to work with new patterns
// export {
//   AggregateListProjector,
//   AggregateListPayload,
//   createAggregateListProjector
// } from './aggregate-list-projector.js'

/**
 * Export query handler types
 */
export type {
  IQuery as IBaseQuery,
  QueryContext,
  QueryResult as IQueryResult,
  QueryExecutionOptions,
  IWaitForSortableUniqueId
} from './types.js'

/**
 * Export handler interfaces
 */
export type {
  IQueryHandler
} from './handler.js'

/**
 * Export handler utilities
 */
export {
  QueryHandler,
  QueryHandler as BaseQueryHandler, // alias for compatibility
  QueryHandlerRegistry,
  createQueryHandler
} from './handler.js'