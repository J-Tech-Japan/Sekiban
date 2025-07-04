import { readdir } from 'fs/promises'
import { join } from 'path'
import { Migration } from '../migration'

/**
 * Load all migrations from the migrations directory
 */
export async function loadMigrations(): Promise<Migration[]> {
  const migrationsDir = join(process.cwd(), 'migrations')
  
  try {
    const files = await readdir(migrationsDir)
    const migrations: Migration[] = []

    for (const file of files) {
      if (file.endsWith('.ts') || file.endsWith('.js')) {
        const filepath = join(migrationsDir, file)
        const module = await import(filepath)
        const migration = module.default || module[Object.keys(module)[0]]
        
        if (migration && migration.id) {
          migrations.push(migration)
        }
      }
    }

    // Sort migrations by ID (which includes timestamp)
    migrations.sort((a, b) => a.id.localeCompare(b.id))

    return migrations
  } catch (error) {
    // If migrations directory doesn't exist, return empty array
    return []
  }
}