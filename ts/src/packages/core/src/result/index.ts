/**
 * Re-export neverthrow for Result type and utilities
 */
export { Result, Ok, Err, ok, err, ResultAsync, okAsync, errAsync } from 'neverthrow';

/**
 * Export all error types
 */
export * from './errors.js';

/**
 * Export error utilities
 */
export * from './error-utils.js';

/**
 * Export error type guards
 */
export * from './error-guards.js';

/**
 * Type alias for Result with SekibanError
 */
import { Result } from 'neverthrow';
import { SekibanError } from './errors.js';

export type SekibanResult<T> = Result<T, SekibanError>;