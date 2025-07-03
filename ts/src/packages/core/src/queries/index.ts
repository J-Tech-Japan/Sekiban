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
  IMultiProjector
} from './multi-projection'

export {
  MultiProjectionState
} from './multi-projection'

/**
 * Export aggregate list projector
 */
export {
  AggregateListProjector,
  AggregateListPayload,
  createAggregateListProjector
} from './aggregate-list-projector'