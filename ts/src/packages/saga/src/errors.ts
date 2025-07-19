import { SekibanError } from '@sekiban/core';

/**
 * Base error for saga-related errors
 */
export abstract class SagaError extends SekibanError {
  abstract readonly code: string;
  
  constructor(
    message: string,
    public readonly sagaId?: string,
    public readonly sagaType?: string,
    public readonly stepName?: string,
    cause?: Error
  ) {
    super(message);
  }
}

/**
 * Error when saga is not found
 */
export class SagaNotFoundError extends SagaError {
  readonly code = 'SAGA_NOT_FOUND';
  
  constructor(sagaId: string) {
    super(`Saga not found: ${sagaId}`, sagaId);
  }
}

/**
 * Error when saga definition is not found
 */
export class SagaDefinitionNotFoundError extends SagaError {
  readonly code = 'SAGA_DEFINITION_NOT_FOUND';
  
  constructor(sagaType: string) {
    super(`Saga definition not found: ${sagaType}`, undefined, sagaType);
  }
}

/**
 * Error when saga step fails
 */
export class SagaStepError extends SagaError {
  readonly code = 'SAGA_STEP_FAILED';
  
  constructor(
    sagaId: string,
    sagaType: string,
    stepName: string,
    cause: Error
  ) {
    super(
      `Saga step failed: ${stepName} in ${sagaType}`,
      sagaId,
      sagaType,
      stepName,
      cause
    );
  }
}

/**
 * Error when saga compensation fails
 */
export class SagaCompensationError extends SagaError {
  readonly code = 'SAGA_COMPENSATION_FAILED';
  
  constructor(
    sagaId: string,
    sagaType: string,
    stepName: string,
    cause: Error
  ) {
    super(
      `Saga compensation failed: ${stepName} in ${sagaType}`,
      sagaId,
      sagaType,
      stepName,
      cause
    );
  }
}

/**
 * Error when saga times out
 */
export class SagaTimeoutError extends SagaError {
  readonly code = 'SAGA_TIMEOUT';
  
  constructor(
    sagaId: string,
    sagaType: string,
    timeoutMs: number
  ) {
    super(
      `Saga timed out after ${timeoutMs}ms: ${sagaType}`,
      sagaId,
      sagaType
    );
  }
}

/**
 * Error when saga is in invalid state for operation
 */
export class SagaInvalidStateError extends SagaError {
  readonly code = 'SAGA_INVALID_STATE';
  
  constructor(
    sagaId: string,
    sagaType: string,
    currentState: string,
    operation: string
  ) {
    super(
      `Invalid saga state for ${operation}: ${currentState}`,
      sagaId,
      sagaType
    );
  }
}

/**
 * General saga error for non-specific errors
 */
export class GeneralSagaError extends SagaError {
  readonly code = 'SAGA_ERROR';
  
  constructor(
    message: string,
    sagaId?: string,
    sagaType?: string,
    stepName?: string,
    cause?: Error
  ) {
    super(message, sagaId, sagaType, stepName, cause);
  }
}