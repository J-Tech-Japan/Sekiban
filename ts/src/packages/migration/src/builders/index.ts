import { Migration, MigrationContext } from '../migration'

/**
 * Builder for creating schema migrations
 */
export class SchemaMigrationBuilder {
  private migration: Partial<Migration> = {
    type: 'schema'
  }

  id(id: string): this {
    this.migration.id = id
    return this
  }

  version(version: number): this {
    this.migration.version = version
    return this
  }

  description(description: string): this {
    this.migration.description = description
    return this
  }

  up(fn: (context: MigrationContext) => Promise<void>): this {
    this.migration.up = fn
    return this
  }

  down(fn: (context: MigrationContext) => Promise<void>): this {
    this.migration.down = fn
    return this
  }

  build(): Migration {
    if (!this.migration.id || !this.migration.version || !this.migration.description || !this.migration.up) {
      throw new Error('Schema migration requires id, version, description, and up function')
    }
    return this.migration as Migration
  }
}

/**
 * Builder for creating data migrations
 */
export class DataMigrationBuilder {
  private migration: Partial<Migration> = {
    type: 'data'
  }

  id(id: string): this {
    this.migration.id = id
    return this
  }

  version(version: number): this {
    this.migration.version = version
    return this
  }

  description(description: string): this {
    this.migration.description = description
    return this
  }

  up(fn: (context: MigrationContext) => Promise<void>): this {
    this.migration.up = fn
    return this
  }

  down(fn: (context: MigrationContext) => Promise<void>): this {
    this.migration.down = fn
    return this
  }

  build(): Migration {
    if (!this.migration.id || !this.migration.version || !this.migration.description || !this.migration.up) {
      throw new Error('Data migration requires id, version, description, and up function')
    }
    return this.migration as Migration
  }
}

/**
 * Create a new schema migration builder
 */
export function schemaMigration(): SchemaMigrationBuilder {
  return new SchemaMigrationBuilder()
}

/**
 * Create a new data migration builder
 */
export function dataMigration(): DataMigrationBuilder {
  return new DataMigrationBuilder()
}