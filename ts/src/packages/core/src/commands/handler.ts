import { Result, ok, err } from 'neverthrow';
import { ICommand, ICommandWithHandler, CommandContext, CommandResult } from './types';
import { IEventPayload } from '../events/index.js';
import { IAggregatePayload, Aggregate, IProjector } from '../aggregates/index.js';
import { SekibanError, CommandValidationError } from '../result/index.js';
import { Metadata } from '../documents/index.js';

/**
 * Interface for command handlers
 */
export interface ICommandHandler<
  TCommand extends ICommand,
  TPayload extends IAggregatePayload
> {
  /**
   * The command type this handler processes
   */
  commandType: string;
  
  /**
   * The aggregate type this handler works with
   */
  aggregateType: string;
  
  /**
   * Validates the command
   */
  validate(command: TCommand): Result<void, CommandValidationError>;
  
  /**
   * Handles the command and produces events
   */
  handle(
    command: TCommand,
    aggregate: Aggregate<TPayload>,
    context: CommandContext
  ): Result<IEventPayload[], SekibanError>;
}

/**
 * Base class for command handlers
 */
export abstract class CommandHandler<
  TCommand extends ICommand,
  TPayload extends IAggregatePayload
> implements ICommandHandler<TCommand, TPayload> {
  
  constructor(
    public readonly commandType: string,
    public readonly aggregateType: string
  ) {}

  /**
   * Default validation (can be overridden)
   */
  validate(command: TCommand): Result<void, CommandValidationError> {
    return ok(undefined);
  }

  /**
   * Abstract handle method to be implemented
   */
  abstract handle(
    command: TCommand,
    aggregate: Aggregate<TPayload>,
    context: CommandContext
  ): Result<IEventPayload[], SekibanError>;
}

/**
 * Creates a command handler from a function
 */
export function createCommandHandler<
  TCommand extends ICommand,
  TPayload extends IAggregatePayload
>(
  commandType: string,
  aggregateType: string,
  handler: (
    command: TCommand,
    aggregate: Aggregate<TPayload>,
    context: CommandContext
  ) => Result<IEventPayload[], SekibanError>,
  validator?: (command: TCommand) => Result<void, CommandValidationError>
): ICommandHandler<TCommand, TPayload> {
  
  return {
    commandType,
    aggregateType,
    validate: validator || (() => ok(undefined)),
    handle: handler,
  };
}

/**
 * Command handler registry
 */
export class CommandHandlerRegistry {
  private handlers = new Map<string, ICommandHandler<any, any>>();

  /**
   * Registers a command handler
   */
  register<TCommand extends ICommand, TPayload extends IAggregatePayload>(
    handler: ICommandHandler<TCommand, TPayload>
  ): void {
    this.handlers.set(handler.commandType, handler);
  }

  /**
   * Registers multiple handlers
   */
  registerMany(handlers: ICommandHandler<any, any>[]): void {
    handlers.forEach(h => this.register(h));
  }

  /**
   * Gets a handler for a command type
   */
  get<TCommand extends ICommand, TPayload extends IAggregatePayload>(
    commandType: string
  ): ICommandHandler<TCommand, TPayload> | undefined {
    return this.handlers.get(commandType);
  }

  /**
   * Checks if a handler is registered
   */
  has(commandType: string): boolean {
    return this.handlers.has(commandType);
  }

  /**
   * Gets all registered command types
   */
  getCommandTypes(): string[] {
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
 * Adapter to use ICommandWithHandler as ICommandHandler
 */
export class CommandWithHandlerAdapter<
  TCommand extends ICommandWithHandler<TCommand, TPayload>,
  TPayload extends IAggregatePayload
> implements ICommandHandler<TCommand, TPayload> {
  
  constructor(
    public readonly commandType: string,
    public readonly aggregateType: string
  ) {}

  validate(command: TCommand): Result<void, CommandValidationError> {
    return command.validate ? command.validate() : ok(undefined);
  }

  handle(
    command: TCommand,
    aggregate: Aggregate<TPayload>,
    context: CommandContext
  ): Result<IEventPayload[], SekibanError> {
    return command.handle(aggregate);
  }
}