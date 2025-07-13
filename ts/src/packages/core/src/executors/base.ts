import { Result, ok, err } from 'neverthrow';
import { 
  ISekibanExecutor, 
  SekibanExecutorConfig,
  ISekibanTransaction,
  ITransactionalSekibanExecutor 
} from './types.js';
import { 
  ICommand, 
  CommandContext, 
  CommandResult, 
  CommandExecutionOptions,
  ICommandExecutor 
} from '../commands/index.js';
import { 
  IBaseQuery as IQuery, 
  QueryContext, 
  IQueryResult as QueryResult, 
  QueryExecutionOptions,
  IQueryHandler,
  QueryHandlerRegistry 
} from '../queries/index.js';
import { IEventPayload, EventRetrievalInfo, OptionalValue, SortableIdCondition, AggregateGroupStream } from '../events/index.js';
import type { IEventStore as IStorageEventStore } from '../storage/index.js';
import type { IEventStore } from '../events/store.js';
import { PartitionKeys, Metadata } from '../documents/index.js';
import { 
  SekibanError, 
  UnsupportedOperationError,
  EventStoreError,
  QueryExecutionError 
} from '../result/index.js';
import { IAggregateLoader } from '../aggregates/index.js';

/**
 * Base implementation of Sekiban executor
 */
export abstract class SekibanExecutorBase implements ISekibanExecutor {
  constructor(
    protected readonly commandExecutor: ICommandExecutor,
    protected readonly queryHandlerRegistry: QueryHandlerRegistry,
    protected readonly eventStore: IEventStore,
    protected readonly aggregateLoader: IAggregateLoader,
    protected readonly config: SekibanExecutorConfig = {}
  ) {}

  async executeCommand<TCommand extends ICommand>(
    command: TCommand,
    partitionKeys: PartitionKeys,
    options?: CommandExecutionOptions
  ): Promise<Result<CommandResult, SekibanError>> {
    const context: CommandContext = {
      partitionKeys,
      metadata: Metadata.create(),
      aggregateType: this.getAggregateTypeForCommand(command),
    };

    const mergedOptions = {
      ...this.config.defaultCommandOptions,
      ...options,
    };

    return this.commandExecutor.execute(command, context, mergedOptions);
  }

  async executeQuery<TQuery extends IQuery<TResult>, TResult>(
    query: TQuery,
    options?: QueryExecutionOptions
  ): Promise<Result<QueryResult<TResult>, SekibanError>> {
    const handler = this.queryHandlerRegistry.get<TQuery, TResult>(query.queryType);
    if (!handler) {
      return err(new UnsupportedOperationError(`No handler registered for query type: ${query.queryType}`));
    }

    const context: QueryContext = {
      timestamp: new Date(),
    };

    const mergedOptions = {
      ...this.config.defaultQueryOptions,
      ...options,
    };

    const startTime = Date.now();

    // Validate query
    const validationResult = handler.validate(query);
    if (validationResult.isErr()) {
      return err(validationResult.error);
    }

    // Execute query
    const result = await handler.handle(query, context);
    if (result.isErr()) {
      return err(result.error);
    }

    const queryResult: QueryResult<TResult> = {
      data: result.value,
      metadata: {
        durationMs: Date.now() - startTime,
        fromCache: false,
        executedAt: context.timestamp,
      },
    };

    return ok(queryResult);
  }

  async getAggregate<TPayload>(
    partitionKeys: PartitionKeys,
    aggregateType: string
  ): Promise<Result<TPayload | null, SekibanError>> {
    try {
      const aggregate = await this.aggregateLoader.load<any>(partitionKeys, aggregateType);
      return ok(aggregate ? aggregate.payload : null);
    } catch (error) {
      return err(new EventStoreError(
        'load aggregate',
        error instanceof Error ? error.message : 'Unknown error'
      ));
    }
  }

  async getEvents(
    partitionKeys: PartitionKeys,
    aggregateType: string,
    fromVersion?: number
  ): Promise<Result<IEventPayload[], SekibanError>> {
    const eventsResult = await this.eventStore.getEvents(partitionKeys, aggregateType, fromVersion);

    if (eventsResult.isErr()) {
      return err(new EventStoreError('read', eventsResult.error.message));
    }

    return ok(eventsResult.value.map((e: any) => e.payload));
  }

  /**
   * Gets the aggregate type for a command (to be implemented by subclasses)
   */
  protected abstract getAggregateTypeForCommand(command: ICommand): string;
}

/**
 * Transactional Sekiban executor base implementation
 */
export abstract class TransactionalSekibanExecutorBase 
  extends SekibanExecutorBase 
  implements ITransactionalSekibanExecutor {
  
  abstract beginTransaction(): Promise<ISekibanTransaction>;

  async withTransaction<T>(
    operation: (transaction: ISekibanTransaction) => Promise<Result<T, SekibanError>>
  ): Promise<Result<T, SekibanError>> {
    const transaction = await this.beginTransaction();
    
    try {
      const result = await operation(transaction);
      
      if (result.isOk()) {
        const commitResult = await transaction.commit();
        if (commitResult.isErr()) {
          await transaction.rollback();
          return err(commitResult.error);
        }
      } else {
        await transaction.rollback();
      }
      
      return result;
    } catch (error) {
      await transaction.rollback();
      throw error;
    }
  }
}

/**
 * Simple transaction implementation
 */
export class SimpleTransaction implements ISekibanTransaction {
  private commands: Array<{
    command: ICommand;
    partitionKeys: PartitionKeys;
    options?: CommandExecutionOptions;
  }> = [];
  
  private committed = false;
  private rolledBack = false;

  constructor(
    private executor: ISekibanExecutor
  ) {}

  async executeCommand<TCommand extends ICommand>(
    command: TCommand,
    partitionKeys: PartitionKeys,
    options?: CommandExecutionOptions
  ): Promise<Result<CommandResult, SekibanError>> {
    if (this.committed || this.rolledBack) {
      return err(new UnsupportedOperationError('Transaction already completed'));
    }

    // Store command for later execution
    this.commands.push({ command, partitionKeys, options });

    // Return a placeholder result
    return ok({
      aggregateId: partitionKeys.aggregateId,
      version: 0,
      events: [],
      metadata: Metadata.create(),
    });
  }

  async commit(): Promise<Result<void, SekibanError>> {
    if (this.committed || this.rolledBack) {
      return err(new UnsupportedOperationError('Transaction already completed'));
    }

    // Execute all commands
    for (const { command, partitionKeys, options } of this.commands) {
      const result = await this.executor.executeCommand(command, partitionKeys, options);
      if (result.isErr()) {
        return err(result.error);
      }
    }

    this.committed = true;
    return ok(undefined);
  }

  async rollback(): Promise<void> {
    this.rolledBack = true;
    this.commands = [];
  }
}