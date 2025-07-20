import { Result } from 'neverthrow';
import { ICommand, CommandContext, CommandResult, CommandExecutionOptions } from '../commands/index';
import { IBaseQuery as IQuery, QueryContext, IQueryResult as QueryResult, QueryExecutionOptions } from '../queries/index';
import { IEventPayload } from '../events/index';
import { PartitionKeys } from '../documents/index';
import { SekibanError } from '../result/index';

/**
 * Interface for Sekiban executors
 */
export interface ISekibanExecutor {
  /**
   * Executes a command
   */
  executeCommand<TCommand extends ICommand>(
    command: TCommand,
    partitionKeys: PartitionKeys,
    options?: CommandExecutionOptions
  ): Promise<Result<CommandResult, SekibanError>>;
  
  /**
   * Executes a query
   */
  executeQuery<TQuery extends IQuery<TResult>, TResult>(
    query: TQuery,
    options?: QueryExecutionOptions
  ): Promise<Result<QueryResult<TResult>, SekibanError>>;
  
  /**
   * Gets the current state of an aggregate
   */
  getAggregate<TPayload>(
    partitionKeys: PartitionKeys,
    aggregateType: string
  ): Promise<Result<TPayload | null, SekibanError>>;
  
  /**
   * Gets events for an aggregate
   */
  getEvents(
    partitionKeys: PartitionKeys,
    aggregateType: string,
    fromVersion?: number
  ): Promise<Result<IEventPayload[], SekibanError>>;
}

/**
 * Configuration for Sekiban executor
 */
export interface SekibanExecutorConfig {
  /**
   * Default command execution options
   */
  defaultCommandOptions?: CommandExecutionOptions;
  
  /**
   * Default query execution options
   */
  defaultQueryOptions?: QueryExecutionOptions;
  
  /**
   * Enable event publishing
   */
  enableEventPublishing?: boolean;
  
  /**
   * Enable snapshots
   */
  enableSnapshots?: boolean;
  
  /**
   * Snapshot frequency (every N events)
   */
  snapshotFrequency?: number;
}

/**
 * Transaction interface for transactional operations
 */
export interface ISekibanTransaction {
  /**
   * Executes a command within the transaction
   */
  executeCommand<TCommand extends ICommand>(
    command: TCommand,
    partitionKeys: PartitionKeys,
    options?: CommandExecutionOptions
  ): Promise<Result<CommandResult, SekibanError>>;
  
  /**
   * Commits the transaction
   */
  commit(): Promise<Result<void, SekibanError>>;
  
  /**
   * Rolls back the transaction
   */
  rollback(): Promise<void>;
}

/**
 * Interface for transactional executors
 */
export interface ITransactionalSekibanExecutor extends ISekibanExecutor {
  /**
   * Begins a new transaction
   */
  beginTransaction(): Promise<ISekibanTransaction>;
  
  /**
   * Executes operations within a transaction
   */
  withTransaction<T>(
    operation: (transaction: ISekibanTransaction) => Promise<Result<T, SekibanError>>
  ): Promise<Result<T, SekibanError>>;
}