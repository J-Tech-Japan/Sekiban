import { writeFile, mkdir } from 'fs/promises'
import { join } from 'path'
import { existsSync } from 'fs'

/**
 * Generate a new migration file
 */
export async function generateMigration(name: string, type: 'schema' | 'data'): Promise<string> {
  const timestamp = new Date().toISOString().replace(/[-:T.]/g, '').slice(0, 14)
  const filename = `${timestamp}_${name}.ts`
  const migrationsDir = join(process.cwd(), 'migrations')

  // Ensure migrations directory exists
  if (!existsSync(migrationsDir)) {
    await mkdir(migrationsDir, { recursive: true })
  }

  const filepath = join(migrationsDir, filename)

  const template = type === 'schema' 
    ? generateSchemaTemplate(name, timestamp)
    : generateDataTemplate(name, timestamp)

  await writeFile(filepath, template)

  return filename
}

function generateSchemaTemplate(name: string, timestamp: string): string {
  return `import { Migration, MigrationContext } from '@sekiban/migration'

export const ${name}Migration: Migration = {
  id: '${timestamp}_${name}',
  type: 'schema',
  version: 1, // Update this to match your schema version
  description: 'TODO: Describe what this migration does',
  
  async up(context: MigrationContext): Promise<void> {
    // TODO: Implement schema changes
    // Example for PostgreSQL:
    // const client = await context.storageProvider.getConnection()
    // await client.query('ALTER TABLE events ADD COLUMN new_field TEXT')
    
    throw new Error('Migration not implemented')
  },
  
  async down(context: MigrationContext): Promise<void> {
    // TODO: Implement rollback logic
    // Example:
    // const client = await context.storageProvider.getConnection()
    // await client.query('ALTER TABLE events DROP COLUMN new_field')
    
    throw new Error('Rollback not implemented')
  }
}

export default ${name}Migration
`
}

function generateDataTemplate(name: string, timestamp: string): string {
  return `import { Migration, MigrationContext } from '@sekiban/migration'
import { EventBatch, PartitionKeys } from '@sekiban/core'

export const ${name}Migration: Migration = {
  id: '${timestamp}_${name}',
  type: 'data',
  version: 1, // Update this to match your data version
  description: 'TODO: Describe what this migration does',
  
  async up(context: MigrationContext): Promise<void> {
    // TODO: Implement data transformation
    // Example:
    // const events = await context.storageProvider.loadEvents(...)
    // for (const event of events) {
    //   if (event.eventType === 'UserCreated' && event.schemaVersion === 1) {
    //     const transformed = transformUserCreatedV1toV2(event)
    //     await context.storageProvider.saveEvents({
    //       partitionKeys: event.partitionKeys,
    //       events: [transformed],
    //       expectedVersion: event.version
    //     })
    //   }
    // }
    
    throw new Error('Migration not implemented')
  }
  
  // Note: down() is optional for data migrations
  // Only implement if the transformation is reversible
}

export default ${name}Migration
`
}