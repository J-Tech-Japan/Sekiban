import { Result } from 'neverthrow'
import { QueryExecutionError } from '../result/errors'

/**
 * Base interface for all queries
 */
export interface IQuery {}

/**
 * Query context for dependency injection
 */
export interface IQueryContext {
  /**
   * Get a service by key
   */
  getService<T>(key: string): T | undefined
}

/**
 * Single result query interface
 */
export interface IMultiProjectionQuery<
  TMultiProjector,
  TQuery extends IQuery,
  TOutput
> extends IQuery {
  // Static method will be defined on the implementing class
}

/**
 * List query interface with filtering and sorting
 */
export interface IMultiProjectionListQuery<
  TMultiProjector,
  TQuery extends IQuery,
  TOutput
> extends IQuery {
  // Static methods will be defined on the implementing class
}

/**
 * Query with paging parameters
 */
export interface IQueryPagingParameter {
  /**
   * Number of items per page
   */
  pageSize: number
  
  /**
   * Current page number (1-based)
   */
  pageNumber: number
}

/**
 * Result of a single query
 */
export interface QueryResult<T> {
  /**
   * The query result value
   */
  value: T
  
  /**
   * The query name
   */
  query?: string
  
  /**
   * The projection version used
   */
  projectionVersion?: number
}

/**
 * Result of a list query with paging
 */
export interface ListQueryResult<T> {
  /**
   * The items in the current page
   */
  items: T[]
  
  /**
   * Total count of all items
   */
  totalCount: number
  
  /**
   * Items per page
   */
  pageSize: number
  
  /**
   * Current page number (1-based)
   */
  pageNumber: number
  
  /**
   * Total number of pages
   */
  totalPages: number
  
  /**
   * Whether there is a next page
   */
  hasNextPage: boolean
  
  /**
   * Whether there is a previous page
   */
  hasPreviousPage: boolean
  
  /**
   * The query name
   */
  query?: string
}

/**
 * Create a query result
 */
export function createQueryResult<T>(options: {
  value: T
  query?: string
  projectionVersion?: number
}): QueryResult<T> {
  return {
    value: options.value,
    query: options.query,
    projectionVersion: options.projectionVersion
  }
}

/**
 * Create a list query result
 */
export function createListQueryResult<T>(options: {
  items: T[]
  totalCount: number
  pageSize: number
  pageNumber: number
  query?: string
}): ListQueryResult<T> {
  const totalPages = Math.ceil(options.totalCount / options.pageSize)
  
  return {
    items: options.items,
    totalCount: options.totalCount,
    pageSize: options.pageSize,
    pageNumber: options.pageNumber,
    totalPages,
    hasNextPage: options.pageNumber < totalPages,
    hasPreviousPage: options.pageNumber > 1,
    query: options.query
  }
}