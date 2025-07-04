import { IEventStorageProvider } from '@sekiban/core'
import { MigrationStore, MigrationRecord } from './store/migration-store'

/**
 * Migration context provided to migration functions
 */
export interface MigrationContext {
  storageProvider: IEventStorageProvider
  migrationStore: MigrationStore
  dryRun: boolean
}

/**
 * Migration definition
 */
export interface Migration {
  id: string
  type: 'schema' | 'data'
  version: number
  description: string
  up(context: MigrationContext): Promise<void>
  down?(context: MigrationContext): Promise<void>
}

/**
 * Progress callback for migration operations
 */
export interface MigrationProgress {
  migrationId: string
  status: 'started' | 'in-progress' | 'completed' | 'failed'
  progress: number
  error?: Error
}

/**
 * Migration runner configuration
 */
export interface MigrationConfig {
  migrationStore: MigrationStore
  storageProvider: IEventStorageProvider
  dryRun?: boolean
  progressCallback?: (progress: MigrationProgress) => void
}

/**
 * Migration status
 */
export interface MigrationStatus {
  currentSchemaVersion: number
  currentDataVersion: number
  appliedMigrations: MigrationRecord[]
  pendingMigrations: Migration[]
}

/**
 * Migration runner
 */
export class MigrationRunner {
  private migrations: Migration[] = []
  
  constructor(private config: MigrationConfig) {}

  /**
   * Run pending migrations
   */
  async up(migrations: Migration[]): Promise<void> {
    const context: MigrationContext = {
      storageProvider: this.config.storageProvider,
      migrationStore: this.config.migrationStore,
      dryRun: this.config.dryRun || false
    }

    // Store migrations for later use in down()
    this.migrations = [...this.migrations, ...migrations]

    for (const migration of migrations) {
      const isApplied = await this.config.migrationStore.hasMigrationBeenApplied(migration.id)
      
      if (isApplied) {
        continue
      }

      this.reportProgress({
        migrationId: migration.id,
        status: 'started',
        progress: 0
      })

      try {
        await migration.up(context)

        if (!this.config.dryRun) {
          await this.config.migrationStore.recordMigration({
            id: migration.id,
            appliedAt: new Date(),
            version: migration.version,
            type: migration.type
          })

          if (migration.type === 'schema') {
            await this.config.migrationStore.setCurrentSchemaVersion(migration.version)
          } else {
            await this.config.migrationStore.setCurrentDataVersion(migration.version)
          }
        }

        this.reportProgress({
          migrationId: migration.id,
          status: 'completed',
          progress: 100
        })
      } catch (error) {
        this.reportProgress({
          migrationId: migration.id,
          status: 'failed',
          progress: 0,
          error: error instanceof Error ? error : new Error(String(error))
        })
        throw error
      }
    }
  }

  /**
   * Rollback migrations
   */
  async down(count: number = 1): Promise<void> {
    const context: MigrationContext = {
      storageProvider: this.config.storageProvider,
      migrationStore: this.config.migrationStore,
      dryRun: this.config.dryRun || false
    }

    const history = await this.config.migrationStore.getMigrationHistory()
    const toRollback = history.slice(-count).reverse()

    for (const record of toRollback) {
      // Find the migration definition
      const migration = this.migrations.find(m => m.id === record.id)
      
      this.reportProgress({
        migrationId: record.id,
        status: 'started',
        progress: 0
      })

      try {
        // Execute down method if available
        if (migration?.down) {
          await migration.down(context)
          
          // Only remove from history if we successfully rolled back
          if (!this.config.dryRun) {
            await this.config.migrationStore.removeMigration(record.id)
          }
        }
        // If no down method, skip rollback and keep in history

        this.reportProgress({
          migrationId: record.id,
          status: 'completed',
          progress: 100
        })
      } catch (error) {
        this.reportProgress({
          migrationId: record.id,
          status: 'failed',
          progress: 0,
          error: error instanceof Error ? error : new Error(String(error))
        })
        throw error
      }
    }
  }

  /**
   * Get migration status
   */
  async status(allMigrations: Migration[]): Promise<MigrationStatus> {
    const currentSchemaVersion = await this.config.migrationStore.getCurrentSchemaVersion()
    const currentDataVersion = await this.config.migrationStore.getCurrentDataVersion()
    const history = await this.config.migrationStore.getMigrationHistory()
    
    const appliedIds = new Set(history.map(h => h.id))
    const pendingMigrations = allMigrations.filter(m => !appliedIds.has(m.id))

    return {
      currentSchemaVersion,
      currentDataVersion,
      appliedMigrations: history,
      pendingMigrations
    }
  }

  private reportProgress(progress: MigrationProgress): void {
    if (this.config.progressCallback) {
      this.config.progressCallback(progress)
    }
  }
}