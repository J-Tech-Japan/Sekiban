/**
 * Export command types
 */
export type {
  CommandExecutionOptions
} from './types'

/**
 * Export legacy command interfaces for backwards compatibility
 * @deprecated Use schema-registry instead
 */
export type {
  ICommand,
} from './command'

export type {
  CommandContext,
  CommandResult,
  ICommandExecutor
} from './executor'

/**
 * Export validation utilities
 */
export type {
  ValidationRule,
  CommandValidator,
  ValidationRules
} from './validation'

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
} from './validation'

/**
 * Export unified command executor
 */
export type {
  CommandExecutionResult,
  UnifiedCommandExecutionOptions,
  IServiceProvider
} from './unified-executor'

export {
  UnifiedCommandExecutor,
  createUnifiedExecutor
} from './unified-executor'

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