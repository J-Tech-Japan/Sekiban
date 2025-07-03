import { Result, ok, err } from '../../../core/src/result';
import { SagaInstance, SagaEvent, SagaStatus } from '../types';
import { SagaError } from '../errors';
import { SagaRepository, SagaSnapshot, SagaSnapshotUtils } from './saga-repository';

/**
 * Adapter that converts between SagaInstance (used by SagaManager) 
 * and SagaSnapshot (used by SagaRepository)
 */
export class SagaStoreAdapter<TContext = any> {
  private events = new Map<string, SagaEvent[]>();

  constructor(private repository: SagaRepository<any>) {}

  /**
   * Save a saga instance to the repository
   */
  async save(instance: SagaInstance<TContext>): Promise<Result<void, SagaError>> {
    try {
      const snapshot = this.instanceToSnapshot(instance);
      await this.repository.save(snapshot);
      return ok(undefined);
    } catch (error) {
      return err(new SagaError(
        `Failed to save saga ${instance.id}`,
        undefined,
        undefined,
        undefined,
        error instanceof Error ? error : undefined
      ));
    }
  }

  /**
   * Load a saga instance from the repository
   */
  async load(sagaId: string): Promise<Result<SagaInstance<TContext> | null, SagaError>> {
    try {
      const snapshot = await this.repository.load(sagaId);
      if (!snapshot) {
        return ok(null);
      }

      const instance = this.snapshotToInstance(snapshot);
      return ok(instance);
    } catch (error) {
      return err(new SagaError(
        `Failed to load saga ${sagaId}`,
        undefined,
        undefined,
        undefined,
        error instanceof Error ? error : undefined
      ));
    }
  }

  /**
   * List saga instances with optional filtering
   */
  async list(filter?: { status?: SagaStatus; sagaType?: string }): Promise<Result<SagaInstance<TContext>[], SagaError>> {
    try {
      const repositoryFilter: any = {};
      
      if (filter?.sagaType) {
        repositoryFilter.sagaType = filter.sagaType;
      }
      
      if (filter?.status) {
        // Map saga status to repository status
        const statusMap: Record<SagaStatus, 'running' | 'completed' | 'failed'> = {
          [SagaStatus.Running]: 'running',
          [SagaStatus.Completed]: 'completed',
          [SagaStatus.Failed]: 'failed',
          [SagaStatus.Compensating]: 'running',
          [SagaStatus.Compensated]: 'failed',
          [SagaStatus.Cancelled]: 'failed',
          [SagaStatus.TimedOut]: 'failed'
        };
        repositoryFilter.status = statusMap[filter.status];
      }

      const snapshots = await this.repository.list(repositoryFilter);
      const instances = snapshots
        .map(snapshot => this.snapshotToInstance(snapshot))
        .filter(instance => {
          // Additional filtering for specific SagaStatus values
          if (filter?.status && instance.state.status !== filter.status) {
            return false;
          }
          return true;
        });

      return ok(instances);
    } catch (error) {
      return err(new SagaError(
        'Failed to list sagas',
        undefined,
        undefined,
        undefined,
        error instanceof Error ? error : undefined
      ));
    }
  }

  /**
   * Save a saga event (stored in memory for now)
   */
  async saveEvent(event: SagaEvent): Promise<Result<void, SagaError>> {
    try {
      const sagaEvents = this.events.get(event.sagaId) || [];
      sagaEvents.push(event);
      this.events.set(event.sagaId, sagaEvents);
      return ok(undefined);
    } catch (error) {
      return err(new SagaError(
        `Failed to save event for saga ${event.sagaId}`,
        undefined,
        undefined,
        undefined,
        error instanceof Error ? error : undefined
      ));
    }
  }

  /**
   * Get events for a saga
   */
  getEvents(sagaId: string): SagaEvent[] {
    return this.events.get(sagaId) || [];
  }

  /**
   * Clear events for a saga (useful for cleanup)
   */
  clearEvents(sagaId: string): void {
    this.events.delete(sagaId);
  }

  /**
   * Convert SagaInstance to SagaSnapshot
   */
  private instanceToSnapshot(instance: SagaInstance<TContext>): SagaSnapshot<any> {
    // Calculate expiration based on saga definition timeout
    let expiresAt: Date | undefined;
    if (instance.definition.timeout) {
      expiresAt = new Date(instance.state.startedAt.getTime() + instance.definition.timeout);
    }

    return {
      id: instance.id,
      state: {
        ...instance.state,
        definition: {
          name: instance.definition.name,
          version: instance.definition.version,
          compensationStrategy: instance.definition.compensationStrategy,
          timeout: instance.definition.timeout
        },
        events: this.getEvents(instance.id)
      },
      version: this.calculateVersion(instance),
      createdAt: instance.createdAt,
      updatedAt: instance.updatedAt,
      expiresAt,
      sagaType: instance.definition.name,
      metadata: {
        definitionVersion: instance.definition.version,
        events: this.getEvents(instance.id)
      }
    };
  }

  /**
   * Convert SagaSnapshot to SagaInstance
   */
  private snapshotToInstance(snapshot: SagaSnapshot<any>): SagaInstance<TContext> {
    const state = snapshot.state;
    const definition = state.definition || {};
    
    // Restore events from snapshot
    if (state.events) {
      this.events.set(snapshot.id, state.events);
    }

    // Create a minimal saga definition from stored data
    const sagaDefinition = {
      name: definition.name || snapshot.sagaType || 'UnknownSaga',
      version: definition.version || 1,
      trigger: { eventType: 'Unknown' },
      steps: [],
      compensationStrategy: definition.compensationStrategy || 'backward',
      timeout: definition.timeout
    };

    return {
      id: snapshot.id,
      definition: sagaDefinition as any,
      state: {
        sagaId: state.sagaId || snapshot.id,
        sagaType: state.sagaType || snapshot.sagaType || 'UnknownSaga',
        status: state.status || SagaStatus.Running,
        currentStep: state.currentStep || 0,
        startedAt: state.startedAt ? new Date(state.startedAt) : snapshot.createdAt,
        completedAt: state.completedAt ? new Date(state.completedAt) : undefined,
        context: state.context || {},
        completedSteps: state.completedSteps || [],
        compensatedSteps: state.compensatedSteps || [],
        failedStep: state.failedStep || null,
        error: state.error || null
      },
      events: this.getEvents(snapshot.id),
      createdAt: snapshot.createdAt,
      updatedAt: snapshot.updatedAt
    };
  }

  /**
   * Calculate version number for optimistic concurrency control
   */
  private calculateVersion(instance: SagaInstance<TContext>): number {
    // Use a combination of completed steps and updates
    return instance.state.completedSteps.length + 
           (instance.state.compensatedSteps?.length || 0) + 
           (instance.state.currentStep || 0) + 1;
  }
}

/**
 * Factory function to create a SagaStore interface that uses SagaRepository
 */
export function createSagaStore<TContext = any>(
  repository: SagaRepository<any>
): {
  save(instance: SagaInstance<TContext>): Promise<Result<void, SagaError>>;
  load(sagaId: string): Promise<Result<SagaInstance<TContext> | null, SagaError>>;
  list(filter?: { status?: SagaStatus; sagaType?: string }): Promise<Result<SagaInstance<TContext>[], SagaError>>;
  saveEvent(event: SagaEvent): Promise<Result<void, SagaError>>;
} {
  const adapter = new SagaStoreAdapter<TContext>(repository);
  
  return {
    save: (instance: SagaInstance<TContext>) => adapter.save(instance),
    load: (sagaId: string) => adapter.load(sagaId),
    list: (filter?: { status?: SagaStatus; sagaType?: string }) => adapter.list(filter),
    saveEvent: (event: SagaEvent) => adapter.saveEvent(event)
  };
}