import { Result, ok, err } from '../../core/src/result';
import {
  SagaDefinition,
  SagaInstance,
  SagaState,
  SagaStatus,
  SagaEvent,
  CompensationStrategy,
  SagaStep
} from './types';
import {
  ICommand,
  IEventPayload,
  EventDocument,
  SekibanError
} from '../../core/src';
import {
  SagaError,
  SagaNotFoundError,
  SagaDefinitionNotFoundError,
  SagaStepError,
  SagaCompensationError,
  SagaTimeoutError,
  SagaInvalidStateError
} from './errors';

/**
 * Command executor interface
 */
export interface ICommandExecutor {
  execute(command: ICommand & { type: string }): Promise<Result<EventDocument<IEventPayload>[], SekibanError>>;
}

/**
 * Saga persistence interface
 */
export interface ISagaStore {
  save(instance: SagaInstance): Promise<Result<void, SagaError>>;
  load(sagaId: string): Promise<Result<SagaInstance | null, SagaError>>;
  list(filter?: { status?: SagaStatus; sagaType?: string }): Promise<Result<SagaInstance[], SagaError>>;
  saveEvent(event: SagaEvent): Promise<Result<void, SagaError>>;
}

/**
 * Saga manager configuration
 */
export interface SagaManagerConfig {
  commandExecutor: ICommandExecutor;
  sagaStore: ISagaStore;
}

/**
 * Manages saga execution and lifecycle
 */
export class SagaManager {
  private definitions = new Map<string, SagaDefinition<any>>();
  private commandExecutor: ICommandExecutor;
  private sagaStore: ISagaStore;

  constructor(config: SagaManagerConfig) {
    this.commandExecutor = config.commandExecutor;
    this.sagaStore = config.sagaStore;
  }

  /**
   * Register a saga definition
   */
  register<TContext>(definition: SagaDefinition<TContext>): void {
    this.definitions.set(definition.name, definition);
  }

  /**
   * Get a saga definition by name
   */
  getSagaDefinition(name: string): SagaDefinition<any> | undefined {
    return this.definitions.get(name);
  }

  /**
   * Handle an event that might trigger sagas
   */
  async handleEvent(event: EventDocument<IEventPayload>): Promise<Result<void, SagaError>> {
    const triggeredSagas: Array<{ definition: SagaDefinition<any>; sagaId: string }> = [];

    // Check all registered sagas for triggers
    for (const definition of this.definitions.values()) {
      if (definition.trigger.eventType === event.eventType) {
        if (!definition.trigger.filter || definition.trigger.filter(event)) {
          const sagaId = this.generateSagaId(definition.name, event);
          triggeredSagas.push({ definition, sagaId });
        }
      }
    }

    // Start triggered sagas
    for (const { definition, sagaId } of triggeredSagas) {
      const result = await this.startSaga(definition, event, sagaId);
      if (result.isErr()) {
        return err(result.error);
      }
    }

    return ok(undefined);
  }

  /**
   * Start a new saga instance
   */
  private async startSaga<TContext>(
    definition: SagaDefinition<TContext>,
    trigger: EventDocument<IEventPayload>,
    sagaId: string
  ): Promise<Result<void, SagaError>> {
    // Create initial context
    const initialContext = definition.initialContext
      ? definition.initialContext(trigger)
      : {} as TContext;

    // Create saga state
    const state: SagaState<TContext> = {
      sagaId,
      sagaType: definition.name,
      status: SagaStatus.Running,
      currentStep: 0,
      startedAt: new Date(),
      context: initialContext,
      completedSteps: [],
      failedStep: null,
      error: null
    };

    // Create saga instance
    const instance: SagaInstance<TContext> = {
      id: sagaId,
      definition,
      state,
      events: [],
      createdAt: new Date(),
      updatedAt: new Date()
    };

    // Save instance
    const saveResult = await this.sagaStore.save(instance);
    if (saveResult.isErr()) {
      return err(saveResult.error);
    }

    // Emit saga started event
    const startEvent: SagaEvent = {
      sagaId,
      eventType: 'SagaStarted',
      timestamp: new Date(),
      data: {
        sagaType: definition.name,
        trigger: {
          eventType: trigger.eventType,
          eventId: trigger.id
        }
      }
    };

    await this.sagaStore.saveEvent(startEvent);

    return ok(undefined);
  }

  /**
   * Execute the next step of a saga
   */
  async executeNextStep(sagaId: string): Promise<Result<void, SagaError>> {
    // Load saga instance
    const loadResult = await this.sagaStore.load(sagaId);
    if (loadResult.isErr()) {
      return err(loadResult.error);
    }

    const instance = loadResult.value;
    if (!instance) {
      return err(new SagaNotFoundError(sagaId));
    }

    // Check if saga is in a state that allows execution
    if (instance.state.status !== SagaStatus.Running) {
      return err(new SagaInvalidStateError(
        sagaId,
        instance.definition.name,
        instance.state.status,
        'executeNextStep'
      ));
    }

    // Check timeout
    if (instance.definition.timeout) {
      const elapsed = Date.now() - instance.state.startedAt.getTime();
      if (elapsed > instance.definition.timeout) {
        return this.timeoutSaga(instance);
      }
    }

    // Get current step
    const currentStepIndex = instance.state.currentStep;
    if (currentStepIndex >= instance.definition.steps.length) {
      return this.completeSaga(instance);
    }

    const step = instance.definition.steps[currentStepIndex];

    // Execute step with retry
    const executeResult = await this.executeStep(instance, step);
    if (executeResult.isErr()) {
      return this.handleStepFailure(instance, step, executeResult.error);
    }

    // Update saga state
    instance.state.completedSteps.push(step.name);
    instance.state.currentStep++;
    instance.state.context = executeResult.value.context;
    instance.updatedAt = new Date();

    // Save updated instance
    const saveResult = await this.sagaStore.save(instance);
    if (saveResult.isErr()) {
      return err(saveResult.error);
    }

    // If this was the last step, complete the saga
    if (instance.state.currentStep >= instance.definition.steps.length) {
      return this.completeSaga(instance);
    }

    return ok(undefined);
  }

  /**
   * Execute a single step with retry logic
   */
  private async executeStep<TContext>(
    instance: SagaInstance<TContext>,
    step: SagaStep<TContext>
  ): Promise<Result<{ context: TContext; events: EventDocument<IEventPayload>[] }, Error>> {
    // Check condition
    if (step.condition && !step.condition(instance.state.context)) {
      return ok({ context: instance.state.context, events: [] });
    }

    // Handle parallel steps
    if (step.parallel) {
      return this.executeParallelSteps(instance, step);
    }

    // Execute command
    if (!step.command) {
      return ok({ context: instance.state.context, events: [] });
    }

    const retryPolicy = step.retryPolicy || { maxAttempts: 1, backoffMs: 0 };
    let lastError: Error | null = null;

    for (let attempt = 0; attempt < retryPolicy.maxAttempts; attempt++) {
      if (attempt > 0) {
        await this.delay(this.calculateBackoff(attempt, retryPolicy));
      }

      const command = step.command(instance.state.context);
      const commandResult = await this.commandExecutor.execute(command);

      if (commandResult.isOk()) {
        const events = commandResult.value;
        const newContext = step.onSuccess(instance.state.context, events[0]);

        // Emit step completed event
        await this.sagaStore.saveEvent({
          sagaId: instance.id,
          eventType: 'SagaStepCompleted',
          timestamp: new Date(),
          data: {
            stepName: step.name,
            result: events[0]?.payload,
            duration: 0 // TODO: Calculate actual duration
          }
        });

        return ok({ context: newContext, events });
      }

      lastError = commandResult.error;
    }

    return err(lastError || new Error('Step execution failed'));
  }

  /**
   * Execute parallel steps
   */
  private async executeParallelSteps<TContext>(
    instance: SagaInstance<TContext>,
    parentStep: SagaStep<TContext>
  ): Promise<Result<{ context: TContext; events: EventDocument<IEventPayload>[] }, Error>> {
    if (!parentStep.parallel) {
      return ok({ context: instance.state.context, events: [] });
    }

    const results = await Promise.all(
      parentStep.parallel.map(step => this.executeStep(instance, step))
    );

    // Check if any failed
    const failed = results.find(r => r.isErr());
    if (failed && failed.isErr()) {
      return err(failed.error);
    }

    // Combine results
    let context = instance.state.context;
    const allEvents: EventDocument<IEventPayload>[] = [];

    for (const result of results) {
      if (result.isOk()) {
        context = result.value.context;
        allEvents.push(...result.value.events);
      }
    }

    return ok({ context, events: allEvents });
  }

  /**
   * Handle step failure
   */
  private async handleStepFailure<TContext>(
    instance: SagaInstance<TContext>,
    step: SagaStep<TContext>,
    error: Error
  ): Promise<Result<void, SagaError>> {
    instance.state.status = SagaStatus.Compensating;
    instance.state.failedStep = step.name;
    instance.state.error = error;
    instance.updatedAt = new Date();

    // Save updated state
    const saveResult = await this.sagaStore.save(instance);
    if (saveResult.isErr()) {
      return err(saveResult.error);
    }

    // Emit step failed event
    await this.sagaStore.saveEvent({
      sagaId: instance.id,
      eventType: 'SagaStepFailed',
      timestamp: new Date(),
      data: {
        stepName: step.name,
        error: error.message
      }
    });

    // Start compensation
    return this.compensate(instance.id);
  }

  /**
   * Compensate a failed saga
   */
  async compensate(sagaId: string): Promise<Result<void, SagaError>> {
    // Load saga instance
    const loadResult = await this.sagaStore.load(sagaId);
    if (loadResult.isErr()) {
      return err(loadResult.error);
    }

    const instance = loadResult.value;
    if (!instance) {
      return err(new SagaNotFoundError(sagaId));
    }

    // Initialize compensated steps if not present
    if (!instance.state.compensatedSteps) {
      instance.state.compensatedSteps = [];
    }

    // Emit compensation started event
    await this.sagaStore.saveEvent({
      sagaId: instance.id,
      eventType: 'SagaCompensationStarted',
      timestamp: new Date(),
      data: {
        failedStep: instance.state.failedStep,
        error: instance.state.error?.message,
        stepsToCompensate: instance.state.completedSteps
      }
    });

    // Get steps to compensate based on strategy
    const stepsToCompensate = this.getCompensationSteps(instance);

    // Execute compensation for each step
    for (const stepName of stepsToCompensate) {
      const step = instance.definition.steps.find(s => s.name === stepName);
      if (!step || !step.compensation) {
        continue;
      }

      const compensationCommand = step.compensation(instance.state.context);
      const result = await this.commandExecutor.execute(compensationCommand);

      if (result.isErr()) {
        return err(new SagaCompensationError(
          instance.id,
          instance.definition.name,
          stepName,
          result.error
        ));
      }

      instance.state.compensatedSteps!.push(stepName);

      // Emit step compensated event
      await this.sagaStore.saveEvent({
        sagaId: instance.id,
        eventType: 'SagaStepCompensated',
        timestamp: new Date(),
        data: {
          stepName,
          result: result.value[0]?.payload
        }
      });
    }

    // Update saga status
    instance.state.status = SagaStatus.Compensated;
    instance.updatedAt = new Date();

    // Save final state
    const saveResult = await this.sagaStore.save(instance);
    if (saveResult.isErr()) {
      return err(saveResult.error);
    }

    // Call onCompensated callback
    if (instance.definition.onCompensated) {
      instance.definition.onCompensated(instance.state.context);
    }

    return ok(undefined);
  }

  /**
   * Get steps to compensate based on strategy
   */
  private getCompensationSteps<TContext>(instance: SagaInstance<TContext>): string[] {
    const completedSteps = [...instance.state.completedSteps];

    switch (instance.definition.compensationStrategy) {
      case CompensationStrategy.Backward:
        return completedSteps.reverse();
      case CompensationStrategy.Forward:
        return completedSteps;
      case CompensationStrategy.Parallel:
        // For parallel, order doesn't matter
        return completedSteps;
      case CompensationStrategy.Custom:
        // TODO: Implement custom compensation order
        return completedSteps.reverse();
      default:
        return completedSteps.reverse();
    }
  }

  /**
   * Complete a successful saga
   */
  private async completeSaga<TContext>(
    instance: SagaInstance<TContext>
  ): Promise<Result<void, SagaError>> {
    instance.state.status = SagaStatus.Completed;
    instance.state.completedAt = new Date();
    instance.updatedAt = new Date();

    // Save final state
    const saveResult = await this.sagaStore.save(instance);
    if (saveResult.isErr()) {
      return err(saveResult.error);
    }

    // Emit completed event
    await this.sagaStore.saveEvent({
      sagaId: instance.id,
      eventType: 'SagaCompleted',
      timestamp: new Date(),
      data: {
        duration: instance.state.completedAt.getTime() - instance.state.startedAt.getTime(),
        finalContext: instance.state.context
      }
    });

    // Call onComplete callback
    if (instance.definition.onComplete) {
      instance.definition.onComplete(instance.state.context);
    }

    return ok(undefined);
  }

  /**
   * Timeout a saga
   */
  private async timeoutSaga<TContext>(
    instance: SagaInstance<TContext>
  ): Promise<Result<void, SagaError>> {
    instance.state.status = SagaStatus.TimedOut;
    instance.updatedAt = new Date();

    // Save updated state
    const saveResult = await this.sagaStore.save(instance);
    if (saveResult.isErr()) {
      return err(saveResult.error);
    }

    // Emit timeout event
    await this.sagaStore.saveEvent({
      sagaId: instance.id,
      eventType: 'SagaTimedOut',
      timestamp: new Date(),
      data: {
        timeout: instance.definition.timeout,
        elapsed: Date.now() - instance.state.startedAt.getTime()
      }
    });

    // Call onTimeout callback
    if (instance.definition.onTimeout) {
      instance.definition.onTimeout(instance.state.context);
    }

    return err(new SagaTimeoutError(
      instance.id,
      instance.definition.name,
      instance.definition.timeout!
    ));
  }

  /**
   * Check if a saga has timed out
   */
  async checkTimeout(sagaId: string): Promise<Result<void, SagaError>> {
    const loadResult = await this.sagaStore.load(sagaId);
    if (loadResult.isErr()) {
      return err(loadResult.error);
    }

    const instance = loadResult.value;
    if (!instance) {
      return err(new SagaNotFoundError(sagaId));
    }

    if (instance.definition.timeout && instance.state.status === SagaStatus.Running) {
      const elapsed = Date.now() - instance.state.startedAt.getTime();
      if (elapsed > instance.definition.timeout) {
        return this.timeoutSaga(instance);
      }
    }

    return ok(undefined);
  }

  /**
   * Generate saga ID
   */
  private generateSagaId(sagaType: string, trigger: EventDocument<IEventPayload>): string {
    return `${sagaType}-${trigger.id}-${Date.now()}`;
  }

  /**
   * Calculate backoff delay
   */
  private calculateBackoff(attempt: number, policy: { backoffMs: number; exponential?: boolean; maxBackoffMs?: number }): number {
    if (!policy.exponential) {
      return policy.backoffMs;
    }

    const delay = policy.backoffMs * Math.pow(2, attempt);
    return policy.maxBackoffMs ? Math.min(delay, policy.maxBackoffMs) : delay;
  }

  /**
   * Delay helper
   */
  private delay(ms: number): Promise<void> {
    return new Promise(resolve => setTimeout(resolve, ms));
  }
}