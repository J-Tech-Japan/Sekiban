import { Result } from 'neverthrow';
import { SagaNotFoundError } from '../errors';

/**
 * Persistent snapshot of a saga instance
 */
export interface SagaSnapshot<TState = any> {
  /** Unique saga instance identifier */
  id: string;
  
  /** Current state of the saga */
  state: TState;
  
  /** Version for optimistic concurrency control */
  version: number;
  
  /** When this snapshot was first created */
  createdAt: Date;
  
  /** When this snapshot was last updated */
  updatedAt: Date;
  
  /** Optional expiration time for cleanup */
  expiresAt?: Date;
  
  /** Saga type/definition name */
  sagaType?: string;
  
  /** Additional metadata */
  metadata?: Record<string, any>;
}

/**
 * Repository interface for saga persistence
 */
export interface SagaRepository<TState = any> {
  /**
   * Load a saga snapshot by ID
   */
  load(id: string): Promise<SagaSnapshot<TState> | null>;
  
  /**
   * Save a saga snapshot with optimistic concurrency control
   * @throws Error if version conflict occurs
   */
  save(snapshot: SagaSnapshot<TState>): Promise<void>;
  
  /**
   * Delete a saga snapshot by ID
   */
  delete(id: string): Promise<void>;
  
  /**
   * Find saga IDs that have expired before the given date
   */
  findExpired(before: Date): Promise<string[]>;
  
  /**
   * Find sagas by their completion status
   */
  findByStatus(status: 'running' | 'completed' | 'failed'): Promise<SagaSnapshot<TState>[]>;
  
  /**
   * List all saga snapshots with optional filtering
   */
  list(filter?: SagaRepositoryFilter): Promise<SagaSnapshot<TState>[]>;
}

/**
 * Filter criteria for saga queries
 */
export interface SagaRepositoryFilter {
  sagaType?: string;
  status?: 'running' | 'completed' | 'failed';
  createdAfter?: Date;
  createdBefore?: Date;
  expiringBefore?: Date;
  limit?: number;
  offset?: number;
}

/**
 * Configuration for repository implementations
 */
export interface SagaRepositoryConfig {
  /** Default expiration time for sagas (in milliseconds) */
  defaultExpirationMs?: number;
  
  /** Maximum number of sagas to return in list operations */
  maxListSize?: number;
  
  /** Enable automatic cleanup of expired sagas */
  enableAutoCleanup?: boolean;
  
  /** Cleanup interval in milliseconds */
  cleanupIntervalMs?: number;
}

/**
 * Error thrown when optimistic concurrency control detects a conflict
 */
export class SagaConcurrencyError extends Error {
  constructor(
    public readonly sagaId: string,
    public readonly expectedVersion: number,
    public readonly actualVersion: number
  ) {
    super(`Saga ${sagaId} version conflict: expected ${expectedVersion}, got ${actualVersion}`);
    this.name = 'SagaConcurrencyError';
  }
}

// SagaNotFoundError is imported from errors module

/**
 * Utility functions for working with saga snapshots
 */
export class SagaSnapshotUtils {
  /**
   * Create a new saga snapshot
   */
  static create<TState>(
    id: string, 
    state: TState, 
    options: {
      sagaType?: string;
      expiresAt?: Date;
      metadata?: Record<string, any>;
    } = {}
  ): SagaSnapshot<TState> {
    const now = new Date();
    return {
      id,
      state,
      version: 1,
      createdAt: now,
      updatedAt: now,
      sagaType: options.sagaType,
      expiresAt: options.expiresAt,
      metadata: options.metadata
    };
  }

  /**
   * Update an existing saga snapshot
   */
  static update<TState>(
    snapshot: SagaSnapshot<TState>,
    newState: TState,
    options: {
      expiresAt?: Date;
      metadata?: Record<string, any>;
    } = {}
  ): SagaSnapshot<TState> {
    return {
      ...snapshot,
      state: newState,
      version: snapshot.version + 1,
      updatedAt: new Date(),
      expiresAt: options.expiresAt ?? snapshot.expiresAt,
      metadata: { ...snapshot.metadata, ...options.metadata }
    };
  }

  /**
   * Check if a saga has expired
   */
  static isExpired(snapshot: SagaSnapshot<any>, now: Date = new Date()): boolean {
    return snapshot.expiresAt ? snapshot.expiresAt < now : false;
  }

  /**
   * Determine saga status from state
   */
  static getStatus(snapshot: SagaSnapshot<any>): 'running' | 'completed' | 'failed' {
    const state = snapshot.state;
    
    // Convention-based status detection
    if (state && typeof state === 'object') {
      if ('status' in state) {
        const status = (state as any).status;
        if (status === 'completed' || status === 'failed') {
          return status;
        }
      }
      
      if ('completed' in state && (state as any).completed === true) {
        return 'completed';
      }
      
      if ('error' in state && (state as any).error) {
        return 'failed';
      }
    }
    
    return 'running';
  }
}