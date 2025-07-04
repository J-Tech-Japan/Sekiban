import { Result, ok, err } from 'neverthrow';
import { IQuery, QueryContext, QueryResult, IWaitForSortableUniqueId } from './types';
import { QueryExecutionError } from '../result/index.js';
import { SortableUniqueId } from '../documents/index.js';

/**
 * Interface for query handlers
 */
export interface IQueryHandler<TQuery extends IQuery<TResult>, TResult> {
  /**
   * The query type this handler processes
   */
  queryType: string;
  
  /**
   * Validates the query
   */
  validate(query: TQuery): Result<void, QueryExecutionError>;
  
  /**
   * Handles the query and returns the result
   */
  handle(query: TQuery, context: QueryContext): Promise<Result<TResult, QueryExecutionError>>;
}

/**
 * Base class for query handlers
 */
export abstract class QueryHandler<TQuery extends IQuery<TResult>, TResult> 
  implements IQueryHandler<TQuery, TResult> {
  
  constructor(public readonly queryType: string) {}

  /**
   * Default validation (can be overridden)
   */
  validate(query: TQuery): Result<void, QueryExecutionError> {
    return ok(undefined);
  }

  /**
   * Abstract handle method to be implemented
   */
  abstract handle(
    query: TQuery,
    context: QueryContext
  ): Promise<Result<TResult, QueryExecutionError>>;
}

/**
 * Creates a query handler from a function
 */
export function createQueryHandler<TQuery extends IQuery<TResult>, TResult>(
  queryType: string,
  handler: (query: TQuery, context: QueryContext) => Promise<Result<TResult, QueryExecutionError>>,
  validator?: (query: TQuery) => Result<void, QueryExecutionError>
): IQueryHandler<TQuery, TResult> {
  
  return {
    queryType,
    validate: validator || (() => ok(undefined)),
    handle: handler,
  };
}

/**
 * Query handler registry
 */
export class QueryHandlerRegistry {
  private handlers = new Map<string, IQueryHandler<any, any>>();

  /**
   * Registers a query handler
   */
  register<TQuery extends IQuery<TResult>, TResult>(
    handler: IQueryHandler<TQuery, TResult>
  ): void {
    this.handlers.set(handler.queryType, handler);
  }

  /**
   * Registers multiple handlers
   */
  registerMany(handlers: IQueryHandler<any, any>[]): void {
    handlers.forEach(h => this.register(h));
  }

  /**
   * Gets a handler for a query type
   */
  get<TQuery extends IQuery<TResult>, TResult>(
    queryType: string
  ): IQueryHandler<TQuery, TResult> | undefined {
    return this.handlers.get(queryType);
  }

  /**
   * Checks if a handler is registered
   */
  has(queryType: string): boolean {
    return this.handlers.has(queryType);
  }

  /**
   * Gets all registered query types
   */
  getQueryTypes(): string[] {
    return Array.from(this.handlers.keys());
  }

  /**
   * Clears all handlers
   */
  clear(): void {
    this.handlers.clear();
  }
}

/**
 * Decorator for caching query results
 */
export function withCache<TQuery extends IQuery<TResult>, TResult>(
  handler: IQueryHandler<TQuery, TResult>,
  cache: Map<string, { result: TResult; expiry: Date }>,
  ttlSeconds: number = 300
): IQueryHandler<TQuery, TResult> {
  
  return {
    queryType: handler.queryType,
    validate: handler.validate.bind(handler),
    
    async handle(query: TQuery, context: QueryContext): Promise<Result<TResult, QueryExecutionError>> {
      const cacheKey = JSON.stringify({ queryType: handler.queryType, query });
      const cached = cache.get(cacheKey);
      
      if (cached && cached.expiry > new Date()) {
        return ok(cached.result);
      }
      
      const result = await handler.handle(query, context);
      
      if (result.isOk()) {
        cache.set(cacheKey, {
          result: result.value,
          expiry: new Date(Date.now() + ttlSeconds * 1000),
        });
      }
      
      return result;
    },
  };
}

/**
 * Decorator for waiting for consistency
 */
export function withConsistencyWait<TQuery extends IQuery<TResult>, TResult>(
  handler: IQueryHandler<TQuery, TResult>,
  checkConsistency: (query: TQuery) => Promise<boolean>,
  timeoutMs: number = 5000,
  pollIntervalMs: number = 100
): IQueryHandler<TQuery, TResult> {
  
  return {
    queryType: handler.queryType,
    validate: handler.validate.bind(handler),
    
    async handle(query: TQuery, context: QueryContext): Promise<Result<TResult, QueryExecutionError>> {
      // Check if query requires waiting for consistency
      const waitForId = (query as any as IWaitForSortableUniqueId).waitForSortableUniqueId;
      if (!waitForId) {
        return handler.handle(query, context);
      }
      
      const startTime = Date.now();
      
      while (Date.now() - startTime < timeoutMs) {
        const isConsistent = await checkConsistency(query);
        if (isConsistent) {
          return handler.handle(query, context);
        }
        
        await new Promise(resolve => setTimeout(resolve, pollIntervalMs));
      }
      
      // Timeout reached, execute anyway
      return handler.handle(query, context);
    },
  };
}