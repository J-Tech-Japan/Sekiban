/**
 * Export command interfaces from command.ts
 */
export type {
  ICommand as ITypedCommand,
  IConstrainedPayloadCommand,
  ICreationCommand
} from './command.js'

export {
  ConstrainedPayloadCommand,
  CreationCommand
} from './command.js'

/**
 * Export command interfaces from types.ts
 */
export type {
  ICommand,
  ICommandWithHandler,
  CommandContext,
  CommandResult,
  CommandExecutionOptions,
  CommandEnvelope
} from './types.js'

/**
 * Export command handler interfaces
 */
export type {
  ICommandHandler
} from './handler.js'

export {
  CommandHandlerRegistry,
  createCommandHandler
} from './handler.js'

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
 * Export command executor
 */
export type {
  ICommandExecutor
} from './executor.js'

export {
  CommandExecutorBase
} from './executor.js'