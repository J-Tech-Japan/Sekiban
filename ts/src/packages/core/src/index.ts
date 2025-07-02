/**
 * @sekiban/core - Event Sourcing and CQRS framework for TypeScript
 */

/**
 * Re-export Result types from neverthrow
 */
export { Result, Ok, Err, ok, err, ResultAsync, okAsync, errAsync } from 'neverthrow';
export type { ResultBox } from 'neverthrow';

/**
 * Export all error types
 */
export * from './result';

/**
 * Export utility functions
 */
export * from './utils';

/**
 * Export document types and utilities
 */
export * from './documents';

/**
 * Export event types and implementations
 */
export * from './events';

/**
 * Export aggregate types and implementations
 */
export * from './aggregates';

/**
 * Export command types and implementations
 */
export * from './commands';

/**
 * Export query types and implementations
 */
export * from './queries';

/**
 * Export serialization utilities
 */
export * from './serialization';

/**
 * Export executor types and implementations
 */
export * from './executors';

/**
 * Version information
 */
export const VERSION = '0.0.1';