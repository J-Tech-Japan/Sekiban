/**
 * Export command types
 */
export type {
  CommandExecutionOptions
} from './types.js'

/**
 * Export legacy command interfaces for backwards compatibility
 * @deprecated Use schema-registry instead
 */
export type {
  ICommand,
} from './command.js'

export type {
  CommandContext,
  CommandResult,
  ICommandExecutor
} from './executor.js'

/**
 * Export validation utilities
 */
export type {
  ValidationRule,
  CommandValidator,
  ValidationRules
} from './validation.js'

export {
  createCommandValidator,
  validateCommand,
  required,
  minLength,
  maxLength,
  email,
  range,
  pattern,
  custom
} from './validation.js'

/**
 * Export unified command executor
 */
export type {
  CommandExecutionResult,
  UnifiedCommandExecutionOptions,
  IServiceProvider
} from './unified-executor.js'

export {
  UnifiedCommandExecutor,
  createUnifiedExecutor
} from './unified-executor.js'

/**
 * Legacy exports for backwards compatibility
 * @deprecated Use schema-registry instead
 */
export class CommandExecutorBase {
  constructor() {
    throw new Error('CommandExecutorBase is deprecated. Use schema-registry command handlers instead.');
  }
}

export class CommandHandlerRegistry {
  constructor() {
    throw new Error('CommandHandlerRegistry is deprecated. Use schema-registry command handlers instead.');
  }
}