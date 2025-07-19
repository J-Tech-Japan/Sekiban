import { ICommand, IEventPayload, EventDocument } from '@sekiban/core';

/**
 * Saga execution status
 */
export enum SagaStatus {
  /** Saga is currently running */
  Running = 'running',
  /** Saga completed successfully */
  Completed = 'completed',
  /** Saga failed and needs compensation */
  Failed = 'failed',
  /** Saga is compensating (rolling back) */
  Compensating = 'compensating',
  /** Saga compensation completed */
  Compensated = 'compensated',
  /** Saga was cancelled */
  Cancelled = 'cancelled',
  /** Saga timed out */
  TimedOut = 'timed_out'
}

/**
 * Compensation strategies
 */
export enum CompensationStrategy {
  /** Compensate in reverse order of execution */
  Backward = 'backward',
  /** Compensate in same order as execution */
  Forward = 'forward',
  /** Compensate all steps in parallel */
  Parallel = 'parallel',
  /** Custom compensation order */
  Custom = 'custom'
}

/**
 * Saga state representation
 */
export interface SagaState<TContext = any> {
  sagaId: string;
  sagaType: string;
  status: SagaStatus;
  currentStep: number;
  startedAt: Date;
  completedAt?: Date;
  context: TContext;
  completedSteps: string[];
  compensatedSteps?: string[];
  failedStep?: string | null;
  error?: Error | null;
}

/**
 * Retry policy for saga steps
 */
export interface RetryPolicy {
  maxAttempts: number;
  backoffMs: number;
  exponential?: boolean;
  maxBackoffMs?: number;
}

/**
 * Saga step definition
 */
export interface SagaStep<TContext> {
  name: string;
  condition?: (context: TContext) => boolean;
  command?: (context: TContext) => ICommand & { type: string };
  parallel?: SagaStep<TContext>[];
  compensation?: (context: TContext) => ICommand & { type: string };
  onSuccess: (context: TContext, event?: EventDocument<IEventPayload>) => TContext;
  onFailure?: (context: TContext, error: Error) => TContext;
  retryPolicy?: RetryPolicy;
  timeout?: number;
  optional?: boolean;
}

/**
 * Saga trigger configuration
 */
export interface SagaTrigger {
  eventType: string;
  filter?: (event: EventDocument<IEventPayload>) => boolean;
}

/**
 * Saga metadata
 */
export interface SagaMetadata {
  description?: string;
  tags?: string[];
  priority?: number;
  [key: string]: any;
}

/**
 * Complete saga definition
 */
export interface SagaDefinition<TContext> {
  name: string;
  version: number;
  trigger: SagaTrigger;
  steps: SagaStep<TContext>[];
  compensationStrategy: CompensationStrategy;
  timeout?: number;
  metadata?: SagaMetadata;
  initialContext?: (trigger: EventDocument<IEventPayload>) => TContext;
  onComplete?: (context: TContext) => void;
  onCompensated?: (context: TContext) => void;
  onTimeout?: (context: TContext) => void;
}

/**
 * Saga execution context
 */
export interface SagaContext<TContext = any> {
  sagaId: string;
  sagaType: string;
  state: SagaState<TContext>;
  trigger: EventDocument<IEventPayload>;
}

/**
 * Saga lifecycle events
 */
export interface SagaEvent {
  sagaId: string;
  eventType: 
    | 'SagaStarted' 
    | 'SagaStepStarted' 
    | 'SagaStepCompleted' 
    | 'SagaStepFailed'
    | 'SagaStepCompensated'
    | 'SagaCompleted' 
    | 'SagaFailed'
    | 'SagaCompensationStarted'
    | 'SagaCompensationCompleted'
    | 'SagaTimedOut'
    | 'SagaCancelled';
  timestamp: Date;
  data: any;
}

/**
 * Saga instance for runtime
 */
export interface SagaInstance<TContext = any> {
  id: string;
  definition: SagaDefinition<TContext>;
  state: SagaState<TContext>;
  events: SagaEvent[];
  createdAt: Date;
  updatedAt: Date;
}