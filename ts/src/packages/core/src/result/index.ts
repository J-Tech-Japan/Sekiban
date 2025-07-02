/**
 * Re-export neverthrow for Result type and utilities
 */
export { Result, Ok, Err, ok, err, ResultAsync, okAsync, errAsync } from 'neverthrow';
export type { ResultBox } from 'neverthrow';

/**
 * Export all error types
 */
export * from './errors';

/**
 * Type alias for Result with SekibanError
 */
import { Result } from 'neverthrow';
import { SekibanError } from './errors';

export type SekibanResult<T> = Result<T, SekibanError>;