import { SekibanError } from '../../core/src';

/**
 * Base error for saga-related errors
 */
export class SagaError extends SekibanError {
  constructor(
    message: string,
    public readonly sagaId?: string,
    public readonly sagaType?: string,
    public readonly stepName?: string,
    cause?: Error
  ) {
    super('SAGA_ERROR', message, cause);
  }
}

/**
 * Error when saga is not found
 */
export class SagaNotFoundError extends SagaError {
  constructor(sagaId: string) {
    super(`Saga not found: ${sagaId}`, sagaId);
    this.code = 'SAGA_NOT_FOUND';
  }
}

/**
 * Error when saga definition is not found
 */
export class SagaDefinitionNotFoundError extends SagaError {
  constructor(sagaType: string) {
    super(`Saga definition not found: ${sagaType}`, undefined, sagaType);
    this.code = 'SAGA_DEFINITION_NOT_FOUND';
  }
}

/**
 * Error when saga step fails
 */
export class SagaStepError extends SagaError {
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
    this.code = 'SAGA_STEP_FAILED';
  }
}

/**
 * Error when saga compensation fails
 */
export class SagaCompensationError extends SagaError {
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
    this.code = 'SAGA_COMPENSATION_FAILED';
  }
}

/**
 * Error when saga times out
 */
export class SagaTimeoutError extends SagaError {
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
    this.code = 'SAGA_TIMEOUT';
  }
}

/**
 * Error when saga is in invalid state for operation
 */
export class SagaInvalidStateError extends SagaError {
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
    this.code = 'SAGA_INVALID_STATE';
  }
}