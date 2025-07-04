import { ok, err, type Result } from 'neverthrow';
import type { ICommandTypes, CommandTypeInfo, CommandMetadata, CommandResult } from '../interfaces.js';
import type { ICommand } from '../../commands/command.js';
import type { IAggregatePayload } from '../../aggregates/aggregate-payload.js';
import type { CommandExecutor } from '../../executors/command-executor.js';
import type { SekibanError } from '../../errors/sekiban-error.js';
import { CommandValidationError } from '../../errors/command-validation-error.js';

export class CommandTypesImpl implements ICommandTypes {
  constructor(
    private readonly commands: Map<string, new (...args: any[]) => ICommand<IAggregatePayload>>
  ) {}

  getCommandTypes(): Array<CommandTypeInfo> {
    return Array.from(this.commands.entries()).map(([name, constructor]) => ({
      name,
      constructor
    }));
  }

  getCommandTypeByName(name: string): (new (...args: any[]) => ICommand<IAggregatePayload>) | undefined {
    return this.commands.get(name);
  }

  async executeCommand(
    executor: CommandExecutor,
    command: unknown,
    metadata: CommandMetadata
  ): Promise<Result<CommandResult, SekibanError>> {
    try {
      // If command is already an ICommand instance, execute it directly
      if (this.isCommand(command)) {
        const result = await executor.execute(command);
        if (result.isOk()) {
          return ok({
            success: true,
            events: result.value.events,
            aggregateId: result.value.aggregate.partitionKeys.aggregateId,
            version: result.value.aggregate.version,
            error: undefined
          });
        } else {
          return err(result.error);
        }
      }

      // If command is an object with commandType property, try to instantiate it
      if (typeof command === 'object' && command !== null && 'commandType' in command) {
        const commandType = (command as any).commandType;
        const CommandConstructor = this.commands.get(commandType);
        
        if (!CommandConstructor) {
          return err(new CommandValidationError(
            commandType,
            [`Command type '${commandType}' not found in registry`]
          ));
        }

        // Try to create command instance
        const commandInstance = Object.assign(
          Object.create(CommandConstructor.prototype),
          command
        ) as ICommand<IAggregatePayload>;

        const result = await executor.execute(commandInstance);
        if (result.isOk()) {
          return ok({
            success: true,
            events: result.value.events,
            aggregateId: result.value.aggregate.partitionKeys.aggregateId,
            version: result.value.aggregate.version,
            error: undefined
          });
        } else {
          return err(result.error);
        }
      }

      return err(new CommandValidationError(
        'Unknown',
        ['Invalid command format. Command must be an ICommand instance or have a commandType property.']
      ));
    } catch (error) {
      return err(new CommandValidationError(
        'Unknown',
        [`Failed to execute command: ${error instanceof Error ? error.message : 'Unknown error'}`]
      ));
    }
  }

  private isCommand(value: unknown): value is ICommand<IAggregatePayload> {
    return (
      typeof value === 'object' &&
      value !== null &&
      'commandType' in value &&
      'specifyPartitionKeys' in value &&
      'validate' in value &&
      'handle' in value
    );
  }
}