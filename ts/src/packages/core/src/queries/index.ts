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
} from './query'

export {
  createQueryResult,
  createListQueryResult
} from './query'

/**
 * Export multi-projection interfaces
 */
export type {
  ISimpleMultiProjector
} from './multi-projection'

export {
  SimpleMultiProjectionState
} from './multi-projection'

// Note: aggregate-list-projector has been removed.
// For list queries, use IMultiProjectionListQuery instead.

/**
 * Export aggregate list projector
 */
export {
  AggregateListProjector
} from './aggregate-list-projector'

/**
 * Export query handler types
 */
export type {
  IQuery as IBaseQuery,
  QueryContext,
  QueryResult as IQueryResult,
  QueryExecutionOptions,
  IWaitForSortableUniqueId
} from './types'

/**
 * Export handler interfaces
 */
export type {
  IQueryHandler
} from './handler'

/**
 * Export handler utilities
 */
export {
  QueryHandler,
  QueryHandler as BaseQueryHandler, // alias for compatibility
  QueryHandlerRegistry,
  createQueryHandler
} from './handler'