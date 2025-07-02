import { Result } from 'neverthrow';
import { PartitionKeys, Metadata } from '../documents';
import { IEventPayload } from '../events';
import { IAggregatePayload, Aggregate } from '../aggregates';
import { CommandValidationError, SekibanError } from '../result';

/**
 * Base interface for all commands
 */
export interface ICommand {
  /**
   * The type identifier for the command
   */
  readonly commandType: string;
}

/**
 * Interface for commands with built-in handlers
 */
export interface ICommandWithHandler<
  TCommand extends ICommand,
  TPayload extends IAggregatePayload
> extends ICommand {
  /**
   * Validates the command
   */
  validate?(): Result<void, CommandValidationError>;
  
  /**
   * Handles the command and produces events
   */
  handle(aggregate: Aggregate<TPayload>): Result<IEventPayload[], SekibanError>;
}

/**
 * Command context containing metadata and partition keys
 */
export interface CommandContext {
  /**
   * The partition keys for the target aggregate
   */
  partitionKeys: PartitionKeys;
  
  /**
   * Command metadata
   */
  metadata: Metadata;
  
  /**
   * The aggregate type
   */
  aggregateType: string;
}

/**
 * Result of command execution
 */
export interface CommandResult {
  /**
   * The aggregate ID affected
   */
  aggregateId: string;
  
  /**
   * The new version after command execution
   */
  version: number;
  
  /**
   * The events produced
   */
  events: IEventPayload[];
  
  /**
   * Command execution metadata
   */
  metadata: Metadata;
}

/**
 * Command envelope containing command and context
 */
export interface CommandEnvelope<TCommand extends ICommand = ICommand> {
  /**
   * The command to execute
   */
  command: TCommand;
  
  /**
   * The command context
   */
  context: CommandContext;
}

/**
 * Interface for command validators
 */
export interface ICommandValidator<TCommand extends ICommand> {
  /**
   * Validates a command
   */
  validate(command: TCommand): Result<void, CommandValidationError>;
}

/**
 * Interface for command preprocessors
 */
export interface ICommandPreprocessor<TCommand extends ICommand> {
  /**
   * Preprocesses a command before execution
   */
  preprocess(command: TCommand, context: CommandContext): Result<TCommand, SekibanError>;
}

/**
 * Command execution options
 */
export interface CommandExecutionOptions {
  /**
   * Whether to skip validation
   */
  skipValidation?: boolean;
  
  /**
   * Whether to use snapshots for loading aggregates
   */
  useSnapshots?: boolean;
  
  /**
   * Custom metadata to merge with command metadata
   */
  metadata?: Partial<Metadata>;
  
  /**
   * Retry options for transient failures
   */
  retry?: {
    maxAttempts: number;
    delayMs: number;
    backoffMultiplier?: number;
  };
}