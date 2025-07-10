import { 
  SagaRepository, 
  SagaSnapshot, 
  SagaRepositoryFilter, 
  SagaRepositoryConfig,
  SagaConcurrencyError,
  SagaSnapshotUtils
} from './saga-repository';

/**
 * In-memory implementation of SagaRepository for testing and development
 */
export class InMemorySagaRepository<TState = any> implements SagaRepository<TState> {
  private snapshots = new Map<string, SagaSnapshot<TState>>();
  private cleanupTimer?: NodeJS.Timeout;

  constructor(private config: SagaRepositoryConfig = {}) {
    if (config.enableAutoCleanup) {
      this.startAutoCleanup();
    }
  }

  async load(id: string): Promise<SagaSnapshot<TState> | null> {
    const snapshot = this.snapshots.get(id);
    return snapshot ? { ...snapshot } : null; // Return copy to prevent mutation
  }

  async save(snapshot: SagaSnapshot<TState>): Promise<void> {
    const existing = this.snapshots.get(snapshot.id);
    
    // Check for version conflicts (optimistic concurrency control)
    if (existing && existing.version !== snapshot.version - 1) {
      throw new SagaConcurrencyError(
        snapshot.id,
        snapshot.version,
        existing.version
      );
    }

    // Store a copy to prevent external mutation
    this.snapshots.set(snapshot.id, { ...snapshot });
  }

  async delete(id: string): Promise<void> {
    this.snapshots.delete(id);
  }

  async findExpired(before: Date): Promise<string[]> {
    const expired: string[] = [];
    
    for (const [id, snapshot] of this.snapshots.entries()) {
      if (SagaSnapshotUtils.isExpired(snapshot, before)) {
        expired.push(id);
      }
    }
    
    return expired;
  }

  async findByStatus(status: 'running' | 'completed' | 'failed'): Promise<SagaSnapshot<TState>[]> {
    const results: SagaSnapshot<TState>[] = [];
    
    for (const snapshot of this.snapshots.values()) {
      if (SagaSnapshotUtils.getStatus(snapshot) === status) {
        results.push({ ...snapshot }); // Return copy
      }
    }
    
    return results;
  }

  async list(filter: SagaRepositoryFilter = {}): Promise<SagaSnapshot<TState>[]> {
    let results = Array.from(this.snapshots.values());

    // Apply filters
    if (filter.sagaType) {
      results = results.filter(s => s.sagaType === filter.sagaType);
    }

    if (filter.status) {
      results = results.filter(s => SagaSnapshotUtils.getStatus(s) === filter.status);
    }

    if (filter.createdAfter) {
      results = results.filter(s => s.createdAt > filter.createdAfter!);
    }

    if (filter.createdBefore) {
      results = results.filter(s => s.createdAt < filter.createdBefore!);
    }

    if (filter.expiringBefore) {
      results = results.filter(s => 
        s.expiresAt && s.expiresAt < filter.expiringBefore!
      );
    }

    // Sort by creation date (newest first)
    results.sort((a, b) => b.createdAt.getTime() - a.createdAt.getTime());

    // Apply pagination
    if (filter.offset) {
      results = results.slice(filter.offset);
    }

    if (filter.limit) {
      results = results.slice(0, filter.limit);
    }

    // Apply max list size limit
    const maxSize = filter.limit || this.config.maxListSize || 1000;
    if (results.length > maxSize) {
      results = results.slice(0, maxSize);
    }

    // Return copies to prevent mutation
    return results.map(s => ({ ...s }));
  }

  /**
   * Clear all snapshots (useful for testing)
   */
  async clear(): Promise<void> {
    this.snapshots.clear();
  }

  /**
   * Get the current size of the repository
   */
  size(): number {
    return this.snapshots.size;
  }

  /**
   * Clean up expired sagas
   */
  async cleanupExpired(): Promise<number> {
    const now = new Date();
    const expired = await this.findExpired(now);
    
    for (const id of expired) {
      await this.delete(id);
    }
    
    return expired.length;
  }

  /**
   * Start automatic cleanup of expired sagas
   */
  private startAutoCleanup(): void {
    const interval = this.config.cleanupIntervalMs || 60000; // Default 1 minute
    
    this.cleanupTimer = setInterval(async () => {
      try {
        await this.cleanupExpired();
      } catch (error) {
        console.error('Failed to cleanup expired sagas:', error);
      }
    }, interval);
  }

  /**
   * Stop automatic cleanup
   */
  dispose(): void {
    if (this.cleanupTimer) {
      clearInterval(this.cleanupTimer);
      this.cleanupTimer = undefined;
    }
  }
}