import { describe, it, expect, beforeEach } from 'vitest'
import { MigrationStore, InMemoryMigrationStore } from './migration-store'

describe('MigrationStore', () => {
  describe('InMemoryMigrationStore', () => {
    let store: MigrationStore

    beforeEach(() => {
      store = new InMemoryMigrationStore()
    })

    describe('schema version', () => {
      it('should start with version 0', async () => {
        const version = await store.getCurrentSchemaVersion()
        expect(version).toBe(0)
      })

      it('should update schema version', async () => {
        await store.setCurrentSchemaVersion(5)
        const version = await store.getCurrentSchemaVersion()
        expect(version).toBe(5)
      })

      it('should throw error for negative version', async () => {
        await expect(store.setCurrentSchemaVersion(-1)).rejects.toThrow(
          'Schema version must be non-negative'
        )
      })
    })

    describe('data version', () => {
      it('should start with version 0', async () => {
        const version = await store.getCurrentDataVersion()
        expect(version).toBe(0)
      })

      it('should update data version', async () => {
        await store.setCurrentDataVersion(3)
        const version = await store.getCurrentDataVersion()
        expect(version).toBe(3)
      })

      it('should track schema and data versions independently', async () => {
        await store.setCurrentSchemaVersion(10)
        await store.setCurrentDataVersion(5)
        
        expect(await store.getCurrentSchemaVersion()).toBe(10)
        expect(await store.getCurrentDataVersion()).toBe(5)
      })
    })

    describe('migration history', () => {
      it('should record applied migrations', async () => {
        await store.recordMigration({
          id: '20240101_create_users',
          appliedAt: new Date(),
          version: 1,
          type: 'schema'
        })

        const history = await store.getMigrationHistory()
        expect(history).toHaveLength(1)
        expect(history[0].id).toBe('20240101_create_users')
      })

      it('should check if migration has been applied', async () => {
        const migrationId = '20240101_create_users'
        
        expect(await store.hasMigrationBeenApplied(migrationId)).toBe(false)
        
        await store.recordMigration({
          id: migrationId,
          appliedAt: new Date(),
          version: 1,
          type: 'schema'
        })
        
        expect(await store.hasMigrationBeenApplied(migrationId)).toBe(true)
      })

      it('should return history in chronological order', async () => {
        const date1 = new Date('2024-01-01')
        const date2 = new Date('2024-01-02')
        const date3 = new Date('2024-01-03')

        await store.recordMigration({
          id: 'migration_2',
          appliedAt: date2,
          version: 2,
          type: 'data'
        })

        await store.recordMigration({
          id: 'migration_1',
          appliedAt: date1,
          version: 1,
          type: 'schema'
        })

        await store.recordMigration({
          id: 'migration_3',
          appliedAt: date3,
          version: 3,
          type: 'schema'
        })

        const history = await store.getMigrationHistory()
        expect(history[0].id).toBe('migration_1')
        expect(history[1].id).toBe('migration_2')
        expect(history[2].id).toBe('migration_3')
      })
    })
  })
})