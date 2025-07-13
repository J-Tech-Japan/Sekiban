import { Result } from 'neverthrow';
import { SortableUniqueId } from '../documents/index.js';
import { QueryExecutionError } from '../result/index.js';

/**
 * Base interface for all queries
 */
export interface IQuery<TResult> {
  /**
   * The type identifier for the query
   */
  readonly queryType: string;
}

/**
 * Interface for queries that wait for a specific event
 */
export interface IWaitForSortableUniqueId {
  /**
   * The sortable unique ID to wait for
   */
  waitForSortableUniqueId?: string;
}

/**
 * Interface for paginated queries
 */
export interface IPaginatedQuery extends IQuery<any> {
  /**
   * The page number (1-based)
   */
  page?: number;
  
  /**
   * The page size
   */
  pageSize?: number;
}

/**
 * Result for paginated queries
 */
export interface PaginatedResult<T> {
  /**
   * The items in the current page
   */
  items: T[];
  
  /**
   * The total number of items
   */
  totalCount: number;
  
  /**
   * The current page number
   */
  page: number;
  
  /**
   * The page size
   */
  pageSize: number;
  
  /**
   * The total number of pages
   */
  totalPages: number;
}

/**
 * Interface for aggregate queries
 */
export interface IAggregateQuery<TResult> extends IQuery<TResult> {
  /**
   * The aggregate ID to query
   */
  aggregateId: string;
  
  /**
   * The aggregate type to query
   */
  aggregateType: string;
}

/**
 * Interface for multi-projection queries
 */
export interface IMultiProjectionQuery<TResult> extends IQuery<TResult> {
  /**
   * The projection type
   */
  projectionType: string;
}

/**
 * Query context containing metadata
 */
export interface QueryContext {
  /**
   * The user executing the query
   */
  userId?: string;
  
  /**
   * Correlation ID for tracking
   */
  correlationId?: string;
  
  /**
   * Query execution timestamp
   */
  timestamp: Date;
  
  /**
   * Additional context data
   */
  data?: Record<string, unknown>;
}

/**
 * Query execution options
 */
export interface QueryExecutionOptions {
  /**
   * Timeout in milliseconds
   */
  timeoutMs?: number;
  
  /**
   * Whether to use cached results
   */
  useCache?: boolean;
  
  /**
   * Cache TTL in seconds
   */
  cacheTtlSeconds?: number;
  
  /**
   * Whether to wait for consistency
   */
  waitForConsistency?: boolean;
  
  /**
   * Maximum time to wait for consistency in milliseconds
   */
  consistencyTimeoutMs?: number;
}

/**
 * Interface for query validators
 */
export interface IQueryValidator<TQuery extends IQuery<any>> {
  /**
   * Validates a query
   */
  validate(query: TQuery): Result<void, QueryExecutionError>;
}

/**
 * Query result with metadata
 */
export interface QueryResult<T> {
  /**
   * The query result data
   */
  data: T;
  
  /**
   * Query execution metadata
   */
  metadata: {
    /**
     * Execution duration in milliseconds
     */
    durationMs: number;
    
    /**
     * Whether the result was served from cache
     */
    fromCache: boolean;
    
    /**
     * The timestamp when the query was executed
     */
    executedAt: Date;
  };
}