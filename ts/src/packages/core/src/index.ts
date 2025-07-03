/**
 * @sekiban/core - Event Sourcing and CQRS framework for TypeScript
 */

/**
 * Re-export Result types from neverthrow
 */
export { Result, Ok, Err, ok, err, ResultAsync, okAsync, errAsync } from 'neverthrow';

/**
 * Export date producer utilities
 */
export type { ISekibanDateProducer } from './date-producer'
export { SekibanDateProducer, createMockDateProducer, createSequentialDateProducer } from './date-producer'

/**
 * Export UUID utilities
 */
export { 
  generateUuid, 
  createVersion7, 
  isValidUuid, 
  createNamespacedUuid,
  createDeterministicUuid 
} from './utils/uuid'

/**
 * Export validation utilities
 */
export type { ValidationError, ValidationResult, Validator, ZodSchema } from './validation'
export { 
  createValidator,
  isValid,
  getErrors,
  validateOrThrow
} from './validation'

/**
 * Version information
 */
export const VERSION = '0.0.1';