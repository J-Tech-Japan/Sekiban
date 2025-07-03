import { promises as fs } from 'fs';
import { join } from 'path';
import { 
  SagaRepository, 
  SagaSnapshot, 
  SagaRepositoryFilter, 
  SagaRepositoryConfig,
  SagaConcurrencyError,
  SagaSnapshotUtils
} from './saga-repository';

/**
 * Configuration for JSON file-based saga repository
 */
export interface JsonFileSagaRepositoryConfig extends SagaRepositoryConfig {
  /** Directory where saga JSON files are stored */
  dataDirectory: string;
  
  /** File extension for saga files (default: .json) */
  fileExtension?: string;
  
  /** Enable pretty printing of JSON files */
  prettyPrint?: boolean;
  
  /** Enable file locking for concurrent access protection */
  enableFileLocking?: boolean;
}

/**
 * JSON file-based implementation of SagaRepository
 * Each saga is stored as a separate JSON file in the data directory
 */
export class JsonFileSagaRepository<TState = any> implements SagaRepository<TState> {
  private readonly dataDirectory: string;
  private readonly fileExtension: string;
  private readonly prettyPrint: boolean;
  private readonly enableFileLocking: boolean;
  private readonly locks = new Map<string, Promise<any>>();
  private cleanupTimer?: NodeJS.Timeout;

  constructor(private config: JsonFileSagaRepositoryConfig) {
    this.dataDirectory = config.dataDirectory;
    this.fileExtension = config.fileExtension || '.json';
    this.prettyPrint = config.prettyPrint || false;
    this.enableFileLocking = config.enableFileLocking !== false; // Default true
    
    if (config.enableAutoCleanup) {
      this.startAutoCleanup();
    }
  }

  async load(id: string): Promise<SagaSnapshot<TState> | null> {
    try {
      const filePath = this.getFilePath(id);
      const content = await fs.readFile(filePath, 'utf-8');
      const data = JSON.parse(content);
      
      // Restore Date objects
      return {
        ...data,
        createdAt: new Date(data.createdAt),
        updatedAt: new Date(data.updatedAt),
        expiresAt: data.expiresAt ? new Date(data.expiresAt) : undefined
      };
    } catch (error: any) {
      if (error.code === 'ENOENT') {
        return null; // File doesn't exist
      }
      
      // Log error but return null for corrupted files
      console.warn(`Failed to load saga ${id}:`, error.message);
      return null;
    }
  }

  async save(snapshot: SagaSnapshot<TState>): Promise<void> {
    const filePath = this.getFilePath(snapshot.id);
    
    if (this.enableFileLocking) {
      return this.withFileLock(snapshot.id, async () => {
        await this.saveInternal(snapshot, filePath);
      });
    } else {
      await this.saveInternal(snapshot, filePath);
    }
  }

  private async saveInternal(snapshot: SagaSnapshot<TState>, filePath: string): Promise<void> {
    // Ensure data directory exists
    await this.ensureDataDirectory();
    
    // Check for version conflicts
    const existing = await this.load(snapshot.id);
    if (existing && existing.version !== snapshot.version - 1) {
      throw new SagaConcurrencyError(
        snapshot.id,
        snapshot.version,
        existing.version
      );
    }

    // Write to temporary file first, then rename (atomic operation)
    const tempPath = `${filePath}.tmp`;
    const content = this.prettyPrint 
      ? JSON.stringify(snapshot, null, 2)
      : JSON.stringify(snapshot);
    
    await fs.writeFile(tempPath, content, 'utf-8');
    await fs.rename(tempPath, filePath);
  }

  async delete(id: string): Promise<void> {
    try {
      const filePath = this.getFilePath(id);
      await fs.unlink(filePath);
    } catch (error: any) {
      if (error.code !== 'ENOENT') {
        throw error; // Re-throw if not "file not found"
      }
    }
  }

  async findExpired(before: Date): Promise<string[]> {
    const files = await this.listSagaFiles();
    const expired: string[] = [];
    
    for (const file of files) {
      const id = this.extractIdFromFileName(file);
      const snapshot = await this.load(id);
      
      if (snapshot && SagaSnapshotUtils.isExpired(snapshot, before)) {
        expired.push(id);
      }
    }
    
    return expired;
  }

  async findByStatus(status: 'running' | 'completed' | 'failed'): Promise<SagaSnapshot<TState>[]> {
    const files = await this.listSagaFiles();
    const results: SagaSnapshot<TState>[] = [];
    
    for (const file of files) {
      const id = this.extractIdFromFileName(file);
      const snapshot = await this.load(id);
      
      if (snapshot && SagaSnapshotUtils.getStatus(snapshot) === status) {
        results.push(snapshot);
      }
    }
    
    return results;
  }

  async list(filter: SagaRepositoryFilter = {}): Promise<SagaSnapshot<TState>[]> {
    const files = await this.listSagaFiles();
    const results: SagaSnapshot<TState>[] = [];
    
    for (const file of files) {
      const id = this.extractIdFromFileName(file);
      const snapshot = await this.load(id);
      
      if (!snapshot) continue;
      
      // Apply filters
      if (filter.sagaType && snapshot.sagaType !== filter.sagaType) continue;
      if (filter.status && SagaSnapshotUtils.getStatus(snapshot) !== filter.status) continue;
      if (filter.createdAfter && snapshot.createdAt <= filter.createdAfter) continue;
      if (filter.createdBefore && snapshot.createdAt >= filter.createdBefore) continue;
      if (filter.expiringBefore && (!snapshot.expiresAt || snapshot.expiresAt >= filter.expiringBefore)) continue;
      
      results.push(snapshot);
    }
    
    // Sort by creation date (newest first)
    results.sort((a, b) => b.createdAt.getTime() - a.createdAt.getTime());
    
    // Apply pagination
    let paginatedResults = results;
    if (filter.offset) {
      paginatedResults = paginatedResults.slice(filter.offset);
    }
    
    if (filter.limit) {
      paginatedResults = paginatedResults.slice(0, filter.limit);
    }
    
    // Apply max list size limit
    const maxSize = filter.limit || this.config.maxListSize || 1000;
    if (paginatedResults.length > maxSize) {
      paginatedResults = paginatedResults.slice(0, maxSize);
    }
    
    return paginatedResults;
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
   * Get the total number of saga files
   */
  async size(): Promise<number> {
    const files = await this.listSagaFiles();
    return files.length;
  }

  /**
   * Dispose of resources (stop cleanup timer)
   */
  dispose(): void {
    if (this.cleanupTimer) {
      clearInterval(this.cleanupTimer);
      this.cleanupTimer = undefined;
    }
  }

  private getFilePath(id: string): string {
    // Sanitize ID to prevent path traversal attacks
    const sanitizedId = id.replace(/[^a-zA-Z0-9_-]/g, '_');
    return join(this.dataDirectory, `${sanitizedId}${this.fileExtension}`);
  }

  private extractIdFromFileName(fileName: string): string {
    return fileName.replace(this.fileExtension, '');
  }

  private async ensureDataDirectory(): Promise<void> {
    try {
      await fs.mkdir(this.dataDirectory, { recursive: true });
    } catch (error: any) {
      if (error.code !== 'EEXIST') {
        throw error;
      }
    }
  }

  private async listSagaFiles(): Promise<string[]> {
    try {
      const files = await fs.readdir(this.dataDirectory);
      return files.filter(file => file.endsWith(this.fileExtension));
    } catch (error: any) {
      if (error.code === 'ENOENT') {
        return []; // Directory doesn't exist yet
      }
      throw error;
    }
  }

  private async withFileLock<T>(id: string, operation: () => Promise<T>): Promise<T> {
    // Simple in-memory locking - for production, consider using a proper file locking library
    const existingLock = this.locks.get(id);
    
    if (existingLock) {
      await existingLock;
    }
    
    const lockPromise = operation();
    this.locks.set(id, lockPromise);
    
    try {
      const result = await lockPromise;
      return result;
    } finally {
      this.locks.delete(id);
    }
  }

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
}