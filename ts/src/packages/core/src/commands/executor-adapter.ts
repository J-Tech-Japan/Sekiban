import { Result, ok, err } from 'neverthrow';
import type { 
  ICommand, 
  CommandContext, 
  CommandResult, 
  CommandExecutionOptions 
} from './types.js';
import type { ICommandExecutor } from './executor.js';
import type { ICommandWithHandler } from '../schema-registry/command-schema.js';
import type { UnifiedCommandExecutor, IServiceProvider } from './unified-executor.js';
import type { SekibanError } from '../result/errors.js';
import { UnsupportedOperationError } from '../result/errors.js';
import type { IAggregateProjector, ITypedAggregatePayload } from '../aggregates/aggregate-projector.js';

/**
 * Adapter to use UnifiedCommandExecutor with the existing ICommandExecutor interface
 */
export class CommandExecutorAdapter implements ICommandExecutor {
  constructor(
    private readonly unifiedExecutor: UnifiedCommandExecutor,
    private readonly serviceProvider?: IServiceProvider
  ) {}
  
  async execute<TCommand extends ICommand>(
    command: TCommand,
    context: CommandContext,
    options?: CommandExecutionOptions
  ): Promise<Result<CommandResult, SekibanError>> {
    // Check if command implements ICommandWithHandler
    if (this.isCommandWithHandler(command)) {
      // Use unified executor
      const result = await this.unifiedExecutor.execute(command, {
        skipValidation: options?.skipValidation,
        metadata: {
          ...context.metadata,
          ...options?.metadata
        },
        serviceProvider: this.serviceProvider
      });
      
      if (result.isErr()) {
        return err(result.error);
      }
      
      const executionResult = result.value;
      
      return ok({
        aggregateId: executionResult.aggregateId,
        version: executionResult.version,
        events: executionResult.events.map(e => e.payload),
        metadata: executionResult.metadata
      });
    }
    
    // Legacy command not supported
    return err(new UnsupportedOperationError(
      `Command type ${command.commandType} does not implement ICommandWithHandler. ` +
      `Please migrate to the new command definition using defineCommand.`
    ));
  }
  
  async executeMany<TCommand extends ICommand>(
    commands: Array<{ command: TCommand; context: CommandContext }>,
    options?: CommandExecutionOptions
  ): Promise<Result<CommandResult[], SekibanError>> {
    const results: CommandResult[] = [];
    
    for (const { command, context } of commands) {
      const result = await this.execute(command, context, options);
      if (result.isErr()) {
        return err(result.error);
      }
      results.push(result.value);
    }
    
    return ok(results);
  }
  
  /**
   * Type guard to check if command implements ICommandWithHandler
   */
  private isCommandWithHandler<
    TProjector extends IAggregateProjector<TPayloadUnion>,
    TPayloadUnion extends ITypedAggregatePayload
  >(command: any): command is ICommandWithHandler<any, TProjector, TPayloadUnion> {
    return (
      typeof command.getProjector === 'function' &&
      typeof command.specifyPartitionKeys === 'function' &&
      typeof command.validate === 'function' &&
      typeof command.handle === 'function'
    );
  }
}

/**
 * Create an adapter that bridges the old and new command executor interfaces
 */
export function createExecutorAdapter(
  unifiedExecutor: UnifiedCommandExecutor,
  serviceProvider?: IServiceProvider
): ICommandExecutor {
  return new CommandExecutorAdapter(unifiedExecutor, serviceProvider);
}