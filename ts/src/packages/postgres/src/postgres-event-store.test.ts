import { describe, it, expect, beforeAll, afterAll, beforeEach } from 'vitest'
import { GenericContainer, StartedTestContainer } from 'testcontainers'
import { Pool } from 'pg'
import { PostgresEventStore } from './postgres-event-store-v2'
import { 
  IEvent, 
  PartitionKeys, 
  EventBatch,
  ConcurrencyError,
  SortableUniqueId
} from '@sekiban/core'

describe('PostgresEventStore', () => {
  let container: StartedTestContainer
  let pool: Pool
  let eventStore: PostgresEventStore

  beforeAll(async () => {
    // Start PostgreSQL container
    container = await new GenericContainer('postgres:16-alpine')
      .withEnvironment({
        POSTGRES_DB: 'sekiban_test',
        POSTGRES_USER: 'test',
        POSTGRES_PASSWORD: 'test'
      })
      .withExposedPorts(5432)
      .start()

    // Create connection pool
    const host = container.getHost()
    const port = container.getMappedPort(5432)
    
    pool = new Pool({
      host,
      port,
      database: 'sekiban_test',
      user: 'test',
      password: 'test',
      max: 10
    })

    // Initialize event store
    eventStore = new PostgresEventStore(pool)
    await eventStore.initialize().match(
      () => {},
      (error) => { throw error }
    )
  }, 60000)

  afterAll(async () => {
    await pool.end()
    await container.stop()
  })

  beforeEach(async () => {
    // Clear events table before each test
    await pool.query('TRUNCATE TABLE events')
  })

  describe('initialize', () => {
    it('should create events table with correct schema', async () => {
      const result = await pool.query(`
        SELECT column_name, data_type, is_nullable
        FROM information_schema.columns
        WHERE table_name = 'events'
        ORDER BY ordinal_position
      `)

      const columns = result.rows
      expect(columns).toContainEqual({
        column_name: 'aggregate_id',
        data_type: 'uuid',
        is_nullable: 'NO'
      })
      expect(columns).toContainEqual({
        column_name: 'seq',
        data_type: 'bigint',
        is_nullable: 'NO'
      })
      expect(columns).toContainEqual({
        column_name: 'event_type',
        data_type: 'text',
        is_nullable: 'NO'
      })
      expect(columns).toContainEqual({
        column_name: 'payload',
        data_type: 'jsonb',
        is_nullable: 'NO'
      })
      expect(columns).toContainEqual({
        column_name: 'meta',
        data_type: 'jsonb',
        is_nullable: 'NO'
      })
      expect(columns).toContainEqual({
        column_name: 'ts',
        data_type: 'timestamp with time zone',
        is_nullable: 'NO'
      })
    })

    it('should create primary key on (aggregate_id, seq)', async () => {
      const result = await pool.query(`
        SELECT conname 
        FROM pg_constraint 
        WHERE conrelid = 'events'::regclass 
        AND contype = 'p'
      `)

      expect(result.rows.length).toBe(1)
      expect(result.rows[0].conname).toBe('events_pkey')
    })

    it('should create index on timestamp', async () => {
      const result = await pool.query(`
        SELECT indexname 
        FROM pg_indexes 
        WHERE tablename = 'events' 
        AND indexname LIKE 'idx_%'
      `)

      expect(result.rows).toContainEqual({ indexname: 'idx_events_ts' })
    })
  })

  describe('saveEvents', () => {
    it('should save a single event', async () => {
      const partitionKeys = PartitionKeys.generate('TestAggregate')
      const event: IEvent = {
        sortableUniqueId: SortableUniqueId.generate(new Date()).value,
        eventType: 'TestEvent',
        payload: { value: 'test' },
        aggregateId: partitionKeys.aggregateId,
        partitionKeys,
        version: 1
      }

      const batch: EventBatch = {
        partitionKeys,
        events: [event],
        expectedVersion: 0
      }

      const result = await eventStore.saveEvents(batch)
      expect(result.isOk()).toBe(true)

      // Verify event was saved
      const dbResult = await pool.query(
        'SELECT * FROM events WHERE aggregate_id = $1',
        [partitionKeys.aggregateId]
      )
      expect(dbResult.rows.length).toBe(1)
      expect(dbResult.rows[0].seq).toBe('1')
      expect(dbResult.rows[0].event_type).toBe('TestEvent')
      expect(dbResult.rows[0].payload).toEqual({ value: 'test' })
    })

    it('should save multiple events in batch', async () => {
      const partitionKeys = PartitionKeys.generate('TestAggregate')
      const events: IEvent[] = [
        {
          sortableUniqueId: SortableUniqueId.generate(new Date()).value,
          eventType: 'TestEvent1',
          payload: { value: 'test1' },
          aggregateId: partitionKeys.aggregateId,
          partitionKeys,
          version: 1
        },
        {
          sortableUniqueId: SortableUniqueId.generate(new Date()).value,
          eventType: 'TestEvent2',
          payload: { value: 'test2' },
          aggregateId: partitionKeys.aggregateId,
          partitionKeys,
          version: 2
        }
      ]

      const batch: EventBatch = {
        partitionKeys,
        events,
        expectedVersion: 0
      }

      const result = await eventStore.saveEvents(batch)
      expect(result.isOk()).toBe(true)

      // Verify events were saved
      const dbResult = await pool.query(
        'SELECT * FROM events WHERE aggregate_id = $1 ORDER BY seq',
        [partitionKeys.aggregateId]
      )
      expect(dbResult.rows.length).toBe(2)
      expect(dbResult.rows[0].seq).toBe('1')
      expect(dbResult.rows[0].event_type).toBe('TestEvent1')
      expect(dbResult.rows[1].seq).toBe('2')
      expect(dbResult.rows[1].event_type).toBe('TestEvent2')
    })

    it('should detect concurrency conflicts', async () => {
      const partitionKeys = PartitionKeys.generate('TestAggregate')
      
      // Save first event
      const event1: IEvent = {
        sortableUniqueId: SortableUniqueId.generate(new Date()).value,
        eventType: 'TestEvent1',
        payload: { value: 'test1' },
        aggregateId: partitionKeys.aggregateId,
        partitionKeys,
        version: 1
      }

      await eventStore.saveEvents({
        partitionKeys,
        events: [event1],
        expectedVersion: 0
      })

      // Try to save with wrong expected version
      const event2: IEvent = {
        sortableUniqueId: SortableUniqueId.generate(new Date()).value,
        eventType: 'TestEvent2',
        payload: { value: 'test2' },
        aggregateId: partitionKeys.aggregateId,
        partitionKeys,
        version: 1
      }

      const result = await eventStore.saveEvents({
        partitionKeys,
        events: [event2],
        expectedVersion: 0 // Wrong! Should be 1
      })

      expect(result.isErr()).toBe(true)
      if (result.isErr()) {
        expect(result.error).toBeInstanceOf(ConcurrencyError)
        // TODO: Fix type issue with ConcurrencyError properties
        // const concurrencyError = result.error as ConcurrencyError
        // expect(concurrencyError.expectedVersion).toBe(0)
        // expect(concurrencyError.actualVersion).toBe(1)
      }
    })
  })

  describe('loadEvents', () => {
    it('should load all events for an aggregate', async () => {
      const partitionKeys = PartitionKeys.generate('TestAggregate')
      
      // Save some events
      const events: IEvent[] = [
        {
          sortableUniqueId: SortableUniqueId.generate(new Date()).value,
          eventType: 'TestEvent1',
          payload: { value: 'test1' },
          aggregateId: partitionKeys.aggregateId,
          partitionKeys,
          version: 1
        },
        {
          sortableUniqueId: SortableUniqueId.generate(new Date()).value,
          eventType: 'TestEvent2',
          payload: { value: 'test2' },
          aggregateId: partitionKeys.aggregateId,
          partitionKeys,
          version: 2
        }
      ]

      await eventStore.saveEvents({
        partitionKeys,
        events,
        expectedVersion: 0
      })

      // Load events
      const result = await eventStore.loadEventsByPartitionKey(partitionKeys)
      expect(result.isOk()).toBe(true)
      
      if (result.isOk()) {
        expect(result.value.length).toBe(2)
        expect(result.value[0].eventType).toBe('TestEvent1')
        expect(result.value[1].eventType).toBe('TestEvent2')
      }
    })

    it('should load events after specific event ID', async () => {
      const partitionKeys = PartitionKeys.generate('TestAggregate')
      
      // Save some events
      const events: IEvent[] = []
      for (let i = 1; i <= 5; i++) {
        events.push({
          sortableUniqueId: SortableUniqueId.generate(new Date()).value,
          eventType: `TestEvent${i}`,
          payload: { value: `test${i}` },
          aggregateId: partitionKeys.aggregateId,
          partitionKeys,
          version: i
        })
      }

      await eventStore.saveEvents({
        partitionKeys,
        events,
        expectedVersion: 0
      })

      // Load events after the 3rd event
      const result = await eventStore.loadEvents(partitionKeys, events[2].sortableUniqueId)
      expect(result.isOk()).toBe(true)
      
      if (result.isOk()) {
        expect(result.value.length).toBe(2)
        expect(result.value[0].eventType).toBe('TestEvent4')
        expect(result.value[1].eventType).toBe('TestEvent5')
      }
    })

    it('should return empty array for non-existent aggregate', async () => {
      const partitionKeys = PartitionKeys.generate('TestAggregate')
      
      const result = await eventStore.loadEventsByPartitionKey(partitionKeys)
      expect(result.isOk()).toBe(true)
      
      if (result.isOk()) {
        expect(result.value).toEqual([])
      }
    })
  })

  describe('batch performance', () => {
    it('should efficiently save large batches of events', async () => {
      const partitionKeys = PartitionKeys.generate('TestAggregate')
      const batchSize = 100
      
      const events: IEvent[] = []
      for (let i = 1; i <= batchSize; i++) {
        events.push({
          sortableUniqueId: SortableUniqueId.generate(new Date()).value,
          eventType: 'TestEvent',
          payload: { index: i, data: 'x'.repeat(100) },
          aggregateId: partitionKeys.aggregateId,
          partitionKeys,
          version: i
        })
      }

      const start = Date.now()
      const result = await eventStore.saveEvents({
        partitionKeys,
        events,
        expectedVersion: 0
      })
      const duration = Date.now() - start

      expect(result.isOk()).toBe(true)
      expect(duration).toBeLessThan(1000) // Should complete within 1 second

      // Verify all events were saved
      const dbResult = await pool.query(
        'SELECT COUNT(*) as count FROM events WHERE aggregate_id = $1',
        [partitionKeys.aggregateId]
      )
      expect(dbResult.rows[0].count).toBe(String(batchSize))
    })
  })
})