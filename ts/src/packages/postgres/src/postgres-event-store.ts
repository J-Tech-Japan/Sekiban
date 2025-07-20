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
          // Create events table if it doesn't exist (matching C# DbEvent structure)
          await this.pool.query(`
            CREATE TABLE IF NOT EXISTS events (
              id UUID PRIMARY KEY,
              payload JSON NOT NULL,
              sortable_unique_id VARCHAR(255) NOT NULL,
              version INTEGER NOT NULL,
              aggregate_id UUID NOT NULL,
              root_partition_key VARCHAR(255) NOT NULL,
              "timestamp" TIMESTAMP NOT NULL,
              partition_key VARCHAR(255) NOT NULL,
              aggregate_group VARCHAR(255) NOT NULL,
              payload_type_name VARCHAR(255) NOT NULL,
              causation_id VARCHAR(255) NOT NULL DEFAULT '',
              correlation_id VARCHAR(255) NOT NULL DEFAULT '',
              executed_user VARCHAR(255) NOT NULL DEFAULT ''
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
            CREATE INDEX IF NOT EXISTS idx_events_timestamp 
            ON events("timestamp")
          `);
          
          await this.pool.query(`
            CREATE INDEX IF NOT EXISTS idx_events_sortable_unique_id 
            ON events(sortable_unique_id)
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
    
    // Parse events from JSON payload (matching C# structure)
    let events = result.rows.map(row => JSON.parse(row.payload) as IEvent);
    
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

    // Base query - select all columns to match C# structure
    let query = 'SELECT * FROM events';

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
    console.log('PostgresEventStore.saveEvents called');
    const result = await ResultAsync.fromPromise(
      this.doSaveEvents(events),
      (error) => new StorageError(
        `Failed to save events: ${error instanceof Error ? error.message : 'Unknown error'}`,
        'SAVE_FAILED',
        error instanceof Error ? error : undefined
      )
    );
    
    if (result.isErr()) {
      throw result.error;
    }
  }
  
  private async doSaveEvents<TEvent extends IEvent>(events: TEvent[]): Promise<void> {
    console.log('doSaveEvents called with', events.length, 'events');
    let client: PoolClient | null = null;
    
    try {
      client = await this.pool.connect();
      
      // Start transaction
      await client.query('BEGIN');
      
      try {
        // Insert each event (matching C# DbEvent structure)
        for (const event of events) {
          // Extract metadata with defaults
          const metadata = event.metadata || {};
          const causationId = metadata.causationId || '';
          const correlationId = metadata.correlationId || '';
          const executedUser = metadata.executedUser || '';
          
          const insertQuery = `INSERT INTO events (
              id, payload, sortable_unique_id,
              version, aggregate_id, root_partition_key,
              "timestamp", partition_key, aggregate_group,
              payload_type_name, causation_id, correlation_id,
              executed_user
            ) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13)`;
          
          const insertParams = [
              event.id.toString(),
              JSON.stringify(event.payload || event.eventData || event),  // Store the event data as payload
              event.sortableUniqueId.toString(),
              event.version,
              event.aggregateId,
              event.partitionKeys?.rootPartitionKey || 'default',
              event.timestamp || new Date(),
              event.partitionKeys?.partitionKey || event.partitionKey,
              event.partitionKeys?.group || event.aggregateGroup || 'default',
              event.eventType,  // payload_type_name
              causationId,
              correlationId,
              executedUser
            ];
          
          console.log('Executing query:', insertQuery);
          console.log('With params:', insertParams);
          
          try {
            await client.query(insertQuery, insertParams);
          } catch (queryError: any) {
            console.error('SQL Error executing INSERT:', queryError.message);
            console.error('SQL Error Code:', queryError.code);
            console.error('SQL Error Position:', queryError.position);
            console.error('SQL Error Detail:', queryError.detail);
            throw queryError;
          }
        }
        
        // Commit transaction
        await client.query('COMMIT');
      } catch (error) {
        // Rollback on error
        await client.query('ROLLBACK');
        throw error;
      }
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