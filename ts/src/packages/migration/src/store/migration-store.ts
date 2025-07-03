/**
 * Migration record
 */
export interface MigrationRecord {
  id: string
  appliedAt: Date
  version: number
  type: 'schema' | 'data'
}

/**
 * Migration store interface for tracking schema and data versions
 */
export interface MigrationStore {
  /**
   * Get current schema version
   */
  getCurrentSchemaVersion(): Promise<number>

  /**
   * Set current schema version
   */
  setCurrentSchemaVersion(version: number): Promise<void>

  /**
   * Get current data version
   */
  getCurrentDataVersion(): Promise<number>

  /**
   * Set current data version
   */
  setCurrentDataVersion(version: number): Promise<void>

  /**
   * Record that a migration has been applied
   */
  recordMigration(migration: MigrationRecord): Promise<void>

  /**
   * Get migration history
   */
  getMigrationHistory(): Promise<MigrationRecord[]>

  /**
   * Check if a migration has been applied
   */
  hasMigrationBeenApplied(migrationId: string): Promise<boolean>
}

/**
 * In-memory implementation of migration store for testing
 */
export class InMemoryMigrationStore implements MigrationStore {
  private schemaVersion = 0
  private dataVersion = 0
  private migrations: MigrationRecord[] = []

  async getCurrentSchemaVersion(): Promise<number> {
    return this.schemaVersion
  }

  async setCurrentSchemaVersion(version: number): Promise<void> {
    if (version < 0) {
      throw new Error('Schema version must be non-negative')
    }
    this.schemaVersion = version
  }

  async getCurrentDataVersion(): Promise<number> {
    return this.dataVersion
  }

  async setCurrentDataVersion(version: number): Promise<void> {
    if (version < 0) {
      throw new Error('Data version must be non-negative')
    }
    this.dataVersion = version
  }

  async recordMigration(migration: MigrationRecord): Promise<void> {
    this.migrations.push(migration)
  }

  async getMigrationHistory(): Promise<MigrationRecord[]> {
    return [...this.migrations].sort((a, b) => 
      a.appliedAt.getTime() - b.appliedAt.getTime()
    )
  }

  async hasMigrationBeenApplied(migrationId: string): Promise<boolean> {
    return this.migrations.some(m => m.id === migrationId)
  }
}