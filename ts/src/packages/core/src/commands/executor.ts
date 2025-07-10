import { Result } from 'neverthrow';
import { SekibanError } from '../result/errors.js';
import { ICommand } from './command.js';
import { CommandExecutionOptions } from './types.js';
import { PartitionKeys, Metadata } from '../documents/index.js';

/**
 * Legacy command context interface
 * @deprecated Use schema-registry command handlers instead
 */
export interface CommandContext {
  partitionKeys: PartitionKeys;
  metadata: Metadata;
  aggregateType: string;
}

/**
 * Legacy command result interface
 * @deprecated Use schema-registry command handlers instead
 */
export interface CommandResult {
  aggregateId: string;
  version: number;
  events: any[];
  metadata: Metadata;
}

/**
 * Legacy command executor interface
 * @deprecated Use schema-registry command handlers instead
 */
export interface ICommandExecutor {
  execute<TCommand extends ICommand>(
    command: TCommand,
    context: CommandContext,
    options?: CommandExecutionOptions
  ): Promise<Result<CommandResult, SekibanError>>;
}