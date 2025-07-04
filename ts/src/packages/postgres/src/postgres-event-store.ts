import { Pool, PoolClient } from 'pg';
import { ResultAsync, okAsync, errAsync } from 'neverthrow';
import {
  IEvent,
  IEventReader,
  IEventWriter,
  IEventStore,
  EventRetrievalInfo,
  StorageError,
  ConnectionError,
  SortableUniqueId
} from '@sekiban/core';

/**
 * PostgreSQL implementation of IEventStore
 * Implements both IEventReader and IEventWriter interfaces
 */
export class PostgresEventStore implements IEventStore {
  constructor(private pool: Pool) {}

  /**
   * Initialize the storage provider
   */
  initialize(): ResultAsync<void, StorageError> {
    return ResultAsync.fromPromise(
      (async () => {
        try {
          // Create events table if it doesn't exist
          await this.pool.query(`
            CREATE TABLE IF NOT EXISTS events (
              id VARCHAR(255) PRIMARY KEY,
              partition_key VARCHAR(255) NOT NULL,
              data JSONB NOT NULL,
              root_partition_key VARCHAR(255) NOT NULL,
              aggregate_group VARCHAR(255) NOT NULL,
              aggregate_id VARCHAR(255) NOT NULL,
              aggregate_type VARCHAR(255) NOT NULL,
              event_type VARCHAR(255) NOT NULL,
              version INTEGER NOT NULL,
              created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            )
          `);

          // Create indexes for efficient querying
          await this.pool.query(`
            CREATE INDEX IF NOT EXISTS idx_events_partition_key 
            ON events(partition_key)
          `);
          
          await this.pool.query(`
            CREATE INDEX IF NOT EXISTS idx_events_root_partition 
            ON events(root_partition_key)
          `);
          
          await this.pool.query(`
            CREATE INDEX IF NOT EXISTS idx_events_aggregate 
            ON events(aggregate_group, aggregate_id)
          `);
          
          await this.pool.query(`
            CREATE INDEX IF NOT EXISTS idx_events_created_at 
            ON events(created_at)
          `);
        } catch (error) {
          throw new ConnectionError(
            `Failed to initialize PostgreSQL: ${error instanceof Error ? error.message : 'Unknown error'}`,
            error instanceof Error ? error : undefined
          );
        }
      })(),
      (error) => error instanceof StorageError ? error : new ConnectionError(
        `Failed to initialize PostgreSQL: ${error instanceof Error ? error.message : 'Unknown error'}`,
        error instanceof Error ? error : undefined
      )
    );
  }

  /**
   * Get events based on retrieval information
   */
  getEvents(eventRetrievalInfo: EventRetrievalInfo): ResultAsync<readonly IEvent[], Error> {
    return ResultAsync.fromPromise(
      this.doGetEvents(eventRetrievalInfo),
      (error) => new StorageError(
        `Failed to query events: ${error instanceof Error ? error.message : 'Unknown error'}`,
        'QUERY_FAILED',
        error instanceof Error ? error : undefined
      )
    );
  }

  private async doGetEvents(eventRetrievalInfo: EventRetrievalInfo): Promise<readonly IEvent[]> {
    const { query, params } = this.buildQuery(eventRetrievalInfo);
    const result = await this.pool.query(query, params);
    
    // Parse events from JSON
    let events = result.rows.map(row => JSON.parse(row.data) as IEvent);
    
    // Apply sortable ID conditions in memory since PostgreSQL doesn't understand our custom ID format
    if (eventRetrievalInfo.sortableIdCondition) {
      events = events.filter(e => 
        !eventRetrievalInfo.sortableIdCondition.outsideOfRange(e.id)
      );
    }

    // Sort by sortable ID
    events.sort((a, b) => SortableUniqueId.compare(a.id, b.id));

    return events;
  }

  private buildQuery(eventRetrievalInfo: EventRetrievalInfo): { query: string; params: any[] } {
    const conditions: string[] = [];
    const params: any[] = [];
    let paramIndex = 1;

    // Base query
    let query = 'SELECT data FROM events';

    // Filter by root partition key
    if (eventRetrievalInfo.rootPartitionKey.hasValueProperty) {
      conditions.push(`root_partition_key = $${paramIndex++}`);
      params.push(eventRetrievalInfo.rootPartitionKey.getValue());
    }

    // Filter by aggregate stream (group)
    if (eventRetrievalInfo.aggregateStream.hasValueProperty) {
      const streamNames = eventRetrievalInfo.aggregateStream.getValue().getStreamNames();
      if (streamNames.length === 1) {
        conditions.push(`aggregate_group = $${paramIndex++}`);
        params.push(streamNames[0]);
      } else if (streamNames.length > 1) {
        const placeholders = streamNames.map(() => `$${paramIndex++}`).join(', ');
        conditions.push(`aggregate_group IN (${placeholders})`);
        params.push(...streamNames);
      }
    }

    // Filter by aggregate ID
    if (eventRetrievalInfo.aggregateId.hasValueProperty) {
      conditions.push(`aggregate_id = $${paramIndex++}`);
      params.push(eventRetrievalInfo.aggregateId.getValue());
    }

    // Add WHERE clause if there are conditions
    if (conditions.length > 0) {
      query += ' WHERE ' + conditions.join(' AND ');
    }

    // Order by ID (which is sortable)
    query += ' ORDER BY id ASC';

    // Add LIMIT if specified
    if (eventRetrievalInfo.maxCount.hasValueProperty) {
      query += ` LIMIT $${paramIndex++}`;
      params.push(eventRetrievalInfo.maxCount.getValue());
    }

    return { query, params };
  }

  /**
   * Save events to storage
   */
  async saveEvents<TEvent extends IEvent>(events: TEvent[]): Promise<void> {
    let client: PoolClient | null = null;
    
    try {
      client = await this.pool.connect();
      
      // Start transaction
      await client.query('BEGIN');
      
      try {
        // Insert each event
        for (const event of events) {
          await client.query(
            `INSERT INTO events (
              id, partition_key, data, 
              root_partition_key, aggregate_group, aggregate_id,
              aggregate_type, event_type, version
            ) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)`,
            [
              event.id.toString(),
              event.partitionKeys.partitionKey,
              JSON.stringify(event),
              event.partitionKeys.rootPartitionKey || 'default',
              event.partitionKeys.group || 'default',
              event.partitionKeys.aggregateId,
              event.aggregateType,
              event.eventType,
              event.version
            ]
          );
        }
        
        // Commit transaction
        await client.query('COMMIT');
      } catch (error) {
        // Rollback on error
        await client.query('ROLLBACK');
        throw error;
      }
    } catch (error) {
      throw new StorageError(
        `Failed to save events: ${error instanceof Error ? error.message : 'Unknown error'}`,
        'SAVE_FAILED',
        error instanceof Error ? error : undefined
      );
    } finally {
      if (client) {
        client.release();
      }
    }
  }

  /**
   * Close the storage provider
   */
  close(): ResultAsync<void, StorageError> {
    return ResultAsync.fromPromise(
      this.pool.end(),
      (error) => new StorageError(
        `Failed to close PostgreSQL connection: ${error instanceof Error ? error.message : 'Unknown error'}`,
        'CLOSE_FAILED',
        error instanceof Error ? error : undefined
      )
    );
  }
}