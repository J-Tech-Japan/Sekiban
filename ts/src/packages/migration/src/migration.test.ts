import { describe, it, expect, vi, beforeEach } from 'vitest'
import { 
  Migration, 
  MigrationContext,
  MigrationRunner,
  MigrationConfig
} from './migration'
import { InMemoryMigrationStore } from './store/migration-store'
import { InMemoryStorageProvider } from '@sekiban/core'

describe('Migration', () => {
  describe('Migration interface', () => {
    it('should define a schema migration', () => {
      const migration: Migration = {
        id: '20240101_add_index',
        type: 'schema',
        version: 1,
        description: 'Add index on aggregate_id',
        async up(context: MigrationContext) {
          // Schema migration logic
        },
        async down(context: MigrationContext) {
          // Rollback logic
        }
      }

      expect(migration.id).toBe('20240101_add_index')
      expect(migration.type).toBe('schema')
      expect(migration.up).toBeDefined()
      expect(migration.down).toBeDefined()
    })

    it('should define a data migration', () => {
      const migration: Migration = {
        id: '20240102_split_names',
        type: 'data',
        version: 1,
        description: 'Split fullName into firstName and lastName',
        async up(context: MigrationContext) {
          // Data transformation logic
        }
        // down is optional for data migrations
      }

      expect(migration.id).toBe('20240102_split_names')
      expect(migration.type).toBe('data')
      expect(migration.down).toBeUndefined()
    })
  })

  describe('MigrationRunner', () => {
    let runner: MigrationRunner
    let store: InMemoryMigrationStore
    let storageProvider: InMemoryStorageProvider
    let config: MigrationConfig

    beforeEach(() => {
      store = new InMemoryMigrationStore()
      storageProvider = new InMemoryStorageProvider({
        type: 'InMemory' as any
      })
      config = {
        migrationStore: store,
        storageProvider,
        dryRun: false,
        progressCallback: vi.fn()
      }
      runner = new MigrationRunner(config)
    })

    describe('up', () => {
      it('should run pending migrations', async () => {
        const migration1: Migration = {
          id: '001_first',
          type: 'schema',
          version: 1,
          description: 'First migration',
          up: vi.fn()
        }

        const migration2: Migration = {
          id: '002_second',
          type: 'schema',
          version: 2,
          description: 'Second migration',
          up: vi.fn()
        }

        await runner.up([migration1, migration2])

        expect(migration1.up).toHaveBeenCalled()
        expect(migration2.up).toHaveBeenCalled()
        expect(await store.getCurrentSchemaVersion()).toBe(2)
        expect(await store.hasMigrationBeenApplied('001_first')).toBe(true)
        expect(await store.hasMigrationBeenApplied('002_second')).toBe(true)
      })

      it('should skip already applied migrations', async () => {
        const migration: Migration = {
          id: '001_already_applied',
          type: 'schema',
          version: 1,
          description: 'Already applied',
          up: vi.fn()
        }

        await store.recordMigration({
          id: '001_already_applied',
          appliedAt: new Date(),
          version: 1,
          type: 'schema'
        })

        await runner.up([migration])

        expect(migration.up).not.toHaveBeenCalled()
      })

      it('should respect dry-run mode', async () => {
        const dryRunConfig = { ...config, dryRun: true }
        const dryRunner = new MigrationRunner(dryRunConfig)

        const migration: Migration = {
          id: '001_dry_run',
          type: 'schema',
          version: 1,
          description: 'Dry run test',
          up: vi.fn()
        }

        await dryRunner.up([migration])

        expect(migration.up).toHaveBeenCalled()
        expect(await store.hasMigrationBeenApplied('001_dry_run')).toBe(false)
        expect(await store.getCurrentSchemaVersion()).toBe(0)
      })

      it('should handle migration failures', async () => {
        const migration: Migration = {
          id: '001_failing',
          type: 'schema',
          version: 1,
          description: 'Failing migration',
          up: vi.fn().mockRejectedValue(new Error('Migration failed'))
        }

        await expect(runner.up([migration])).rejects.toThrow('Migration failed')
        expect(await store.hasMigrationBeenApplied('001_failing')).toBe(false)
      })

      it('should call progress callback', async () => {
        const migration: Migration = {
          id: '001_progress',
          type: 'schema',
          version: 1,
          description: 'Progress test',
          up: vi.fn()
        }

        await runner.up([migration])

        expect(config.progressCallback).toHaveBeenCalledWith({
          migrationId: '001_progress',
          status: 'started',
          progress: 0
        })

        expect(config.progressCallback).toHaveBeenCalledWith({
          migrationId: '001_progress',
          status: 'completed',
          progress: 100
        })
      })
    })

    describe('down', () => {
      it('should rollback migrations', async () => {
        const migration: Migration = {
          id: '001_rollback',
          type: 'schema',
          version: 1,
          description: 'Rollback test',
          up: vi.fn(),
          down: vi.fn()
        }

        // First apply the migration
        await runner.up([migration])
        
        // Then roll it back
        await runner.down(1)

        expect(migration.down).toHaveBeenCalled()
        expect(await store.hasMigrationBeenApplied('001_rollback')).toBe(false)
      })

      it('should skip migrations without down method', async () => {
        const migration: Migration = {
          id: '001_no_down',
          type: 'data',
          version: 1,
          description: 'No down method',
          up: vi.fn()
        }

        await runner.up([migration])
        
        // Should not throw
        await runner.down(1)
        
        // Migration should still be marked as applied since it couldn't be rolled back
        expect(await store.hasMigrationBeenApplied('001_no_down')).toBe(true)
      })
    })

    describe('status', () => {
      it('should return migration status', async () => {
        const migrations: Migration[] = [
          {
            id: '001_applied',
            type: 'schema',
            version: 1,
            description: 'Applied migration',
            up: vi.fn()
          },
          {
            id: '002_pending',
            type: 'schema',
            version: 2,
            description: 'Pending migration',
            up: vi.fn()
          }
        ]

        await runner.up([migrations[0]])

        const status = await runner.status(migrations)
        
        expect(status.currentSchemaVersion).toBe(1)
        expect(status.currentDataVersion).toBe(0)
        expect(status.appliedMigrations).toHaveLength(1)
        expect(status.pendingMigrations).toHaveLength(1)
        expect(status.appliedMigrations[0].id).toBe('001_applied')
        expect(status.pendingMigrations[0].id).toBe('002_pending')
      })
    })
  })
})