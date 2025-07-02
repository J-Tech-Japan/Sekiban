import { Result, ok, err } from 'neverthrow';
import { 
  ICommand, 
  CommandContext, 
  CommandResult, 
  CommandExecutionOptions,
  CommandEnvelope 
} from './types';
import { ICommandHandler, CommandHandlerRegistry } from './handler';
import { IEventStore } from '../events/store';
import { IEventStream } from '../events/stream';
import { IAggregateLoader, IProjector } from '../aggregates';
import { 
  SekibanError, 
  CommandValidationError, 
  AggregateNotFoundError,
  UnsupportedOperationError 
} from '../result';
import { PartitionKeys, Metadata } from '../documents';
import { Event, EventBuilder } from '../events';

/**
 * Interface for command executors
 */
export interface ICommandExecutor {
  /**
   * Executes a command
   */
  execute<TCommand extends ICommand>(
    command: TCommand,
    context: CommandContext,
    options?: CommandExecutionOptions
  ): Promise<Result<CommandResult, SekibanError>>;
  
  /**
   * Executes multiple commands in order
   */
  executeMany<TCommand extends ICommand>(
    commands: Array<CommandEnvelope<TCommand>>,
    options?: CommandExecutionOptions
  ): Promise<Result<CommandResult[], SekibanError>>;
}

/**
 * Base command executor implementation
 */
export abstract class CommandExecutorBase implements ICommandExecutor {
  constructor(
    protected readonly handlerRegistry: CommandHandlerRegistry,
    protected readonly eventStore: IEventStore,
    protected readonly eventStream: IEventStream,
    protected readonly aggregateLoader: IAggregateLoader
  ) {}

  async execute<TCommand extends ICommand>(
    command: TCommand,
    context: CommandContext,
    options?: CommandExecutionOptions
  ): Promise<Result<CommandResult, SekibanError>> {
    // Get handler
    const handler = this.handlerRegistry.get<TCommand, any>(command.commandType);
    if (!handler) {
      return err(new UnsupportedOperationError(`No handler registered for command type: ${command.commandType}`));
    }

    // Validate command
    if (!options?.skipValidation) {
      const validationResult = handler.validate(command);
      if (validationResult.isErr()) {
        return err(validationResult.error);
      }
    }

    // Load aggregate
    const aggregate = await this.aggregateLoader.load(
      context.partitionKeys,
      context.aggregateType
    );

    if (!aggregate && context.partitionKeys.aggregateId !== 'new') {
      return err(new AggregateNotFoundError(
        context.partitionKeys.aggregateId,
        context.aggregateType
      ));
    }

    // Get initial aggregate if not found
    const currentAggregate = aggregate || this.getInitialAggregate(
      context.partitionKeys,
      context.aggregateType
    );

    // Handle command
    const eventsResult = handler.handle(command, currentAggregate, context);
    if (eventsResult.isErr()) {
      return err(eventsResult.error);
    }

    const events = eventsResult.value;
    if (events.length === 0) {
      // No events produced, return current state
      return ok({
        aggregateId: context.partitionKeys.aggregateId,
        version: currentAggregate.version,
        events: [],
        metadata: context.metadata,
      });
    }

    // Merge metadata
    const metadata = options?.metadata 
      ? Metadata.merge(context.metadata, options.metadata)
      : context.metadata;

    // Append events to store
    const appendResult = await this.eventStore.appendEvents(
      context.partitionKeys,
      context.aggregateType,
      events,
      currentAggregate.version,
      metadata
    );

    if (appendResult.isErr()) {
      return err(appendResult.error);
    }

    const storedEvents = appendResult.value;

    // Publish events to stream
    await this.eventStream.publishMany(storedEvents);

    // Return result
    return ok({
      aggregateId: context.partitionKeys.aggregateId,
      version: storedEvents[storedEvents.length - 1].version,
      events,
      metadata,
    });
  }

  async executeMany<TCommand extends ICommand>(
    commands: Array<CommandEnvelope<TCommand>>,
    options?: CommandExecutionOptions
  ): Promise<Result<CommandResult[], SekibanError>> {
    const results: CommandResult[] = [];

    for (const envelope of commands) {
      const result = await this.execute(
        envelope.command,
        envelope.context,
        options
      );

      if (result.isErr()) {
        return err(result.error);
      }

      results.push(result.value);
    }

    return ok(results);
  }

  /**
   * Gets the initial aggregate for a given type
   */
  protected abstract getInitialAggregate(
    partitionKeys: PartitionKeys,
    aggregateType: string
  ): any;
}

/**
 * Command executor with retry logic
 */
export class RetryCommandExecutor extends CommandExecutorBase {
  async execute<TCommand extends ICommand>(
    command: TCommand,
    context: CommandContext,
    options?: CommandExecutionOptions
  ): Promise<Result<CommandResult, SekibanError>> {
    const retryOptions = options?.retry || { maxAttempts: 1, delayMs: 0 };
    let lastError: SekibanError | null = null;

    for (let attempt = 1; attempt <= retryOptions.maxAttempts; attempt++) {
      const result = await super.execute(command, context, options);
      
      if (result.isOk()) {
        return result;
      }

      lastError = result.error;

      // Don't retry validation errors
      if (lastError instanceof CommandValidationError) {
        return result;
      }

      // Wait before retry
      if (attempt < retryOptions.maxAttempts) {
        const delay = retryOptions.delayMs * 
          Math.pow(retryOptions.backoffMultiplier || 1, attempt - 1);
        await new Promise(resolve => setTimeout(resolve, delay));
      }
    }

    return err(lastError!);
  }
}