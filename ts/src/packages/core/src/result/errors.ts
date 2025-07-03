/**
 * Base error class for Sekiban framework
 */
export abstract class SekibanError extends Error {
  abstract readonly code: string;
  
  constructor(message: string) {
    super(message);
    this.name = this.constructor.name;
  }
}

/**
 * Error thrown when an aggregate is not found
 */
export class AggregateNotFoundError extends SekibanError {
  readonly code = 'AGGREGATE_NOT_FOUND';
  
  constructor(public readonly aggregateId: string, public readonly aggregateType: string) {
    super(`Aggregate ${aggregateType} with id ${aggregateId} not found`);
  }
}

/**
 * Error thrown when a command validation fails
 */
export class CommandValidationError extends SekibanError {
  readonly code = 'COMMAND_VALIDATION_ERROR';
  
  constructor(public readonly commandType: string, public readonly validationErrors: string[]) {
    super(`Command validation failed for ${commandType}: ${validationErrors.join(', ')}`);
  }
}

/**
 * Error thrown when an event cannot be applied to an aggregate
 */
export class EventApplicationError extends SekibanError {
  readonly code = 'EVENT_APPLICATION_ERROR';
  
  constructor(public readonly eventType: string, public readonly reason: string) {
    super(`Failed to apply event ${eventType}: ${reason}`);
  }
}

/**
 * Error thrown when a query execution fails
 */
export class QueryExecutionError extends SekibanError {
  readonly code = 'QUERY_EXECUTION_ERROR';
  
  constructor(public readonly queryType: string, public readonly reason: string) {
    super(`Query execution failed for ${queryType}: ${reason}`);
  }
}

/**
 * Error thrown when serialization/deserialization fails
 */
export class SerializationError extends SekibanError {
  readonly code = 'SERIALIZATION_ERROR';
  
  constructor(public readonly operation: 'serialize' | 'deserialize', public readonly reason: string) {
    super(`Serialization error during ${operation}: ${reason}`);
  }
}

/**
 * Error thrown when event store operations fail
 */
export class EventStoreError extends SekibanError {
  readonly code = 'EVENT_STORE_ERROR';
  
  constructor(public readonly operation: string, public readonly reason: string) {
    super(`Event store error during ${operation}: ${reason}`);
  }
}

/**
 * Error thrown when a concurrent update conflict occurs
 */
export class ConcurrencyError extends SekibanError {
  readonly code = 'CONCURRENCY_ERROR';
  
  constructor(public readonly expectedVersion: number, public readonly actualVersion: number) {
    super(`Concurrency conflict: expected version ${expectedVersion}, but was ${actualVersion}`);
  }
}

/**
 * Error thrown when an unsupported operation is attempted
 */
export class UnsupportedOperationError extends SekibanError {
  readonly code = 'UNSUPPORTED_OPERATION';
  
  constructor(public readonly operation: string) {
    super(`Unsupported operation: ${operation}`);
  }
}

/**
 * Error thrown when validation fails
 */
export class ValidationError extends SekibanError {
  readonly code = 'VALIDATION_ERROR';
  
  constructor(message: string) {
    super(message);
  }
}