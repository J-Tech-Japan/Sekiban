import { Pool, PoolClient } from 'pg'
import { ResultAsync, okAsync, errAsync } from 'neverthrow'
import {
  IEventStorageProvider,
  EventBatch,
  SnapshotData,
  StorageError,
  ConnectionError,
  ConcurrencyError,
  IEvent,
  PartitionKeys
} from '@sekiban/core'

/**
 * PostgreSQL event store implementation
 */
export class PostgresEventStore implements IEventStorageProvider {
  constructor(private pool: Pool) {}

  /**
   * Initialize the database schema
   */
  initialize(): ResultAsync<void, StorageError> {
    return ResultAsync.fromPromise(
      this.executeTransaction(async (client) => {
        // Create events table
        await client.query(`
          CREATE TABLE IF NOT EXISTS events (
            aggregate_id UUID NOT NULL,
            seq BIGINT NOT NULL,
            event_type TEXT NOT NULL,
            payload JSONB NOT NULL,
            meta JSONB NOT NULL DEFAULT '{}',
            ts TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            sortable_unique_id TEXT NOT NULL,
            PRIMARY KEY (aggregate_id, seq)
          )
        `)

        // Create indexes
        await client.query(`
          CREATE INDEX IF NOT EXISTS idx_events_ts ON events (ts)
        `)
        await client.query(`
          CREATE INDEX IF NOT EXISTS idx_events_sortable_id ON events (sortable_unique_id)
        `)

        // Create snapshots table
        await client.query(`
          CREATE TABLE IF NOT EXISTS snapshots (
            aggregate_id UUID NOT NULL,
            version BIGINT NOT NULL,
            aggregate_type TEXT NOT NULL,
            payload JSONB NOT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            last_event_id TEXT NOT NULL,
            PRIMARY KEY (aggregate_id)
          )
        `)
      }),
      (error) => this.toStorageError(error, 'INITIALIZATION_FAILED', 'Failed to initialize database')
    )
  }

  /**
   * Save events to the database
   */
  saveEvents(batch: EventBatch): ResultAsync<void, StorageError> {
    if (batch.events.length === 0) {
      return okAsync(undefined)
    }

    return ResultAsync.fromPromise(
      this.executeTransaction(async (client) => {
        // Check current version for optimistic concurrency control
        const versionResult = await client.query(
          'SELECT COALESCE(MAX(seq), 0) as current_version FROM events WHERE aggregate_id = $1',
          [batch.partitionKeys.aggregateId]
        )
        const currentVersion = Number(versionResult.rows[0].current_version)

        if (currentVersion !== batch.expectedVersion) {
          throw new ConcurrencyError(
            `Expected version ${batch.expectedVersion} but current version is ${currentVersion}`,
            batch.expectedVersion,
            currentVersion
          )
        }

        // Prepare batch insert
        const values: any[] = []
        const placeholders: string[] = []
        let paramIndex = 1

        batch.events.forEach((event, index) => {
          const seq = currentVersion + index + 1
          const meta = {
            partitionKeys: event.partitionKeys,
            version: event.version
          }

          placeholders.push(
            `($${paramIndex}, $${paramIndex + 1}, $${paramIndex + 2}, $${paramIndex + 3}, $${paramIndex + 4}, $${paramIndex + 5})`
          )
          
          values.push(
            batch.partitionKeys.aggregateId,
            seq,
            event.eventType,
            JSON.stringify(event.payload),
            JSON.stringify(meta),
            event.sortableUniqueId
          )
          
          paramIndex += 6
        })

        // Execute batch insert
        await client.query(
          `INSERT INTO events (aggregate_id, seq, event_type, payload, meta, sortable_unique_id)
           VALUES ${placeholders.join(', ')}`,
          values
        )
      }),
      (error) => {
        if (error instanceof ConcurrencyError) {
          return error
        }
        return this.toStorageError(error, 'SAVE_FAILED', 'Failed to save events')
      }
    )
  }

  /**
   * Load all events for a partition key
   */
  loadEventsByPartitionKey(partitionKeys: PartitionKeys): ResultAsync<IEvent[], StorageError> {
    return this.loadEventsQuery(
      `SELECT event_type, payload, meta, sortable_unique_id, seq
       FROM events
       WHERE aggregate_id = $1
       ORDER BY seq`,
      [partitionKeys.aggregateId],
      partitionKeys
    )
  }

  /**
   * Load events starting after a specific event ID
   */
  loadEvents(partitionKeys: PartitionKeys, afterEventId?: string): ResultAsync<IEvent[], StorageError> {
    if (afterEventId) {
      return this.loadEventsQuery(
        `SELECT event_type, payload, meta, sortable_unique_id, seq
         FROM events
         WHERE aggregate_id = $1 AND sortable_unique_id > $2
         ORDER BY seq`,
        [partitionKeys.aggregateId, afterEventId],
        partitionKeys
      )
    } else {
      return this.loadEventsByPartitionKey(partitionKeys)
    }
  }

  /**
   * Get the latest snapshot for an aggregate
   */
  getLatestSnapshot(partitionKeys: PartitionKeys): ResultAsync<SnapshotData | null, StorageError> {
    return ResultAsync.fromPromise(
      this.executeQuery(async (client) => {
        const result = await client.query(
          `SELECT version, aggregate_type, payload, created_at, last_event_id
           FROM snapshots
           WHERE aggregate_id = $1`,
          [partitionKeys.aggregateId]
        )

        if (result.rows.length === 0) {
          return null
        }

        const row = result.rows[0]
        return {
          partitionKeys,
          version: Number(row.version),
          aggregateType: row.aggregate_type,
          payload: row.payload,
          createdAt: row.created_at,
          lastEventId: row.last_event_id
        }
      }),
      (error) => this.toStorageError(error, 'LOAD_FAILED', 'Failed to load snapshot')
    )
  }

  /**
   * Save a snapshot
   */
  saveSnapshot(snapshot: SnapshotData): ResultAsync<void, StorageError> {
    return ResultAsync.fromPromise(
      this.executeQuery(async (client) => {
        await client.query(
          `INSERT INTO snapshots (aggregate_id, version, aggregate_type, payload, last_event_id)
           VALUES ($1, $2, $3, $4, $5)
           ON CONFLICT (aggregate_id) 
           DO UPDATE SET
             version = EXCLUDED.version,
             aggregate_type = EXCLUDED.aggregate_type,
             payload = EXCLUDED.payload,
             last_event_id = EXCLUDED.last_event_id,
             created_at = NOW()`,
          [
            snapshot.partitionKeys.aggregateId,
            snapshot.version,
            snapshot.aggregateType,
            JSON.stringify(snapshot.payload),
            snapshot.lastEventId
          ]
        )
      }),
      (error) => this.toStorageError(error, 'SAVE_FAILED', 'Failed to save snapshot')
    )
  }

  /**
   * Close the connection pool
   */
  close(): ResultAsync<void, StorageError> {
    return ResultAsync.fromPromise(
      this.pool.end(),
      (error) => this.toStorageError(error, 'CLOSE_FAILED', 'Failed to close connection pool')
    )
  }

  /**
   * Helper method to load events with a query
   */
  private loadEventsQuery(
    query: string,
    params: any[],
    partitionKeys: PartitionKeys
  ): ResultAsync<IEvent[], StorageError> {
    return ResultAsync.fromPromise(
      this.executeQuery(async (client) => {
        const result = await client.query(query, params)

        return result.rows.map(row => ({
          sortableUniqueId: row.sortable_unique_id,
          eventType: row.event_type,
          payload: row.payload,
          aggregateId: partitionKeys.aggregateId,
          partitionKeys: row.meta.partitionKeys || partitionKeys,
          version: row.meta.version || Number(row.seq)
        }))
      }),
      (error) => this.toStorageError(error, 'LOAD_FAILED', 'Failed to load events')
    )
  }

  /**
   * Execute a query with a client from the pool
   */
  private async executeQuery<T>(fn: (client: PoolClient) => Promise<T>): Promise<T> {
    const client = await this.pool.connect()
    try {
      return await fn(client)
    } finally {
      client.release()
    }
  }

  /**
   * Execute a transaction with a client from the pool
   */
  private async executeTransaction<T>(fn: (client: PoolClient) => Promise<T>): Promise<T> {
    const client = await this.pool.connect()
    try {
      await client.query('BEGIN')
      const result = await fn(client)
      await client.query('COMMIT')
      return result
    } catch (error) {
      await client.query('ROLLBACK')
      throw error
    } finally {
      client.release()
    }
  }

  /**
   * Convert an error to a StorageError
   */
  private toStorageError(error: unknown, code: string, message: string): StorageError {
    if (error instanceof StorageError) {
      return error
    }
    return new StorageError(
      `${message}: ${error instanceof Error ? error.message : 'Unknown error'}`,
      code,
      error instanceof Error ? error : undefined
    )
  }
}