#!/usr/bin/env node

import { Command } from 'commander'
import chalk from 'chalk'
import ora from 'ora'
import { MigrationRunner } from '../migration'
import { generateMigration } from './generate'
import { loadMigrations } from './loader'
import { createMigrationStore } from './store-factory'
import { createStorageProvider } from './storage-factory'

const program = new Command()

program
  .name('sekiban-migrate')
  .description('Sekiban migration tool for schema and data evolution')
  .version('0.0.1')

// Up command
program
  .command('up')
  .description('Run all pending migrations')
  .option('--dry-run', 'Preview migrations without applying them')
  .option('--storage <type>', 'Storage provider type (postgres, cosmos, inmemory)', 'postgres')
  .option('--connection <string>', 'Connection string for storage provider')
  .option('--database <string>', 'Database name')
  .action(async (options) => {
    const spinner = ora('Loading migrations...').start()

    try {
      // Load migrations
      const migrations = await loadMigrations()
      spinner.succeed(`Loaded ${migrations.length} migrations`)

      // Create storage provider
      const storageProvider = await createStorageProvider({
        type: options.storage,
        connectionString: options.connection,
        databaseName: options.database
      })

      // Create migration store
      const migrationStore = await createMigrationStore(options.storage, {
        connectionString: options.connection,
        databaseName: options.database
      })

      // Create runner
      const runner = new MigrationRunner({
        storageProvider,
        migrationStore,
        dryRun: options.dryRun,
        progressCallback: (progress) => {
          if (progress.status === 'started') {
            spinner.start(`Running migration ${progress.migrationId}...`)
          } else if (progress.status === 'completed') {
            spinner.succeed(`Completed migration ${progress.migrationId}`)
          } else if (progress.status === 'failed') {
            spinner.fail(`Failed migration ${progress.migrationId}: ${progress.error?.message}`)
          }
        }
      })

      // Get status
      const statusBefore = await runner.status(migrations)
      console.log(chalk.blue(`\nCurrent schema version: ${statusBefore.currentSchemaVersion}`))
      console.log(chalk.blue(`Current data version: ${statusBefore.currentDataVersion}`))
      console.log(chalk.blue(`Pending migrations: ${statusBefore.pendingMigrations.length}\n`))

      if (statusBefore.pendingMigrations.length === 0) {
        console.log(chalk.green('✓ All migrations are up to date'))
        return
      }

      // Run migrations
      await runner.up(migrations)

      if (options.dryRun) {
        console.log(chalk.yellow('\n✓ Dry run completed successfully'))
      } else {
        console.log(chalk.green('\n✓ All migrations completed successfully'))
      }
    } catch (error) {
      spinner.fail('Migration failed')
      console.error(chalk.red(error instanceof Error ? error.message : String(error)))
      process.exit(1)
    }
  })

// Down command
program
  .command('down [count]')
  .description('Rollback migrations')
  .option('--storage <type>', 'Storage provider type', 'postgres')
  .option('--connection <string>', 'Connection string for storage provider')
  .option('--database <string>', 'Database name')
  .action(async (count = '1', options) => {
    console.log(chalk.yellow('Rollback functionality not fully implemented yet'))
    // Implementation would be similar to up command
  })

// Status command
program
  .command('status')
  .description('Show migration status')
  .option('--storage <type>', 'Storage provider type', 'postgres')
  .option('--connection <string>', 'Connection string for storage provider')
  .option('--database <string>', 'Database name')
  .action(async (options) => {
    try {
      const migrations = await loadMigrations()
      const storageProvider = await createStorageProvider({
        type: options.storage,
        connectionString: options.connection,
        databaseName: options.database
      })
      const migrationStore = await createMigrationStore(options.storage, {
        connectionString: options.connection,
        databaseName: options.database
      })

      const runner = new MigrationRunner({
        storageProvider,
        migrationStore
      })

      const status = await runner.status(migrations)

      console.log(chalk.blue('\n=== Migration Status ===\n'))
      console.log(`Schema Version: ${status.currentSchemaVersion}`)
      console.log(`Data Version: ${status.currentDataVersion}`)
      console.log(`\nApplied Migrations (${status.appliedMigrations.length}):`)
      
      status.appliedMigrations.forEach(m => {
        console.log(chalk.green(`  ✓ ${m.id} (${m.type} v${m.version})`))
      })

      if (status.pendingMigrations.length > 0) {
        console.log(`\nPending Migrations (${status.pendingMigrations.length}):`)
        status.pendingMigrations.forEach(m => {
          console.log(chalk.yellow(`  ○ ${m.id} (${m.type} v${m.version})`))
        })
      } else {
        console.log(chalk.green('\n✓ All migrations are up to date'))
      }
    } catch (error) {
      console.error(chalk.red(error instanceof Error ? error.message : String(error)))
      process.exit(1)
    }
  })

// Generate command
program
  .command('generate <name>')
  .description('Generate a new migration file')
  .option('--type <type>', 'Migration type (schema or data)', 'data')
  .action(async (name, options) => {
    try {
      const filename = await generateMigration(name, options.type)
      console.log(chalk.green(`✓ Created migration: ${filename}`))
    } catch (error) {
      console.error(chalk.red(error instanceof Error ? error.message : String(error)))
      process.exit(1)
    }
  })

program.parse()