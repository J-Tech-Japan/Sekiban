import {
  SekibanError,
  AggregateNotFoundError,
  CommandValidationError,
  EventApplicationError,
  QueryExecutionError,
  SerializationError,
  EventStoreError,
  ConcurrencyError,
  UnsupportedOperationError,
  ValidationError
} from './errors.js'

/**
 * Type guard for SekibanError
 */
export function isSekibanError(error: unknown): error is SekibanError {
  return error instanceof SekibanError
}

/**
 * Type guard for AggregateNotFoundError
 */
export function isAggregateNotFoundError(error: unknown): error is AggregateNotFoundError {
  return error instanceof AggregateNotFoundError
}

/**
 * Type guard for CommandValidationError
 */
export function isCommandValidationError(error: unknown): error is CommandValidationError {
  return error instanceof CommandValidationError
}

/**
 * Type guard for EventApplicationError
 */
export function isEventApplicationError(error: unknown): error is EventApplicationError {
  return error instanceof EventApplicationError
}

/**
 * Type guard for QueryExecutionError
 */
export function isQueryExecutionError(error: unknown): error is QueryExecutionError {
  return error instanceof QueryExecutionError
}

/**
 * Type guard for SerializationError
 */
export function isSerializationError(error: unknown): error is SerializationError {
  return error instanceof SerializationError
}

/**
 * Type guard for EventStoreError
 */
export function isEventStoreError(error: unknown): error is EventStoreError {
  return error instanceof EventStoreError
}

/**
 * Type guard for ConcurrencyError
 */
export function isConcurrencyError(error: unknown): error is ConcurrencyError {
  return error instanceof ConcurrencyError
}

/**
 * Type guard for UnsupportedOperationError
 */
export function isUnsupportedOperationError(error: unknown): error is UnsupportedOperationError {
  return error instanceof UnsupportedOperationError
}

/**
 * Type guard for ValidationError
 */
export function isValidationError(error: unknown): error is ValidationError {
  return error instanceof ValidationError
}