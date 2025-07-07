import { Pool } from 'pg';
import { PostgresEventStore } from '@sekiban/postgres';

async function testWithNewTable() {
  const pool = new Pool({
    host: 'localhost',
    port: 5432,
    database: 'sekiban_events',
    user: 'sekiban',
    password: 'sekiban_password'
  });

  try {
    // Drop old events table
    console.log('Dropping old events table...');
    await pool.query('DROP TABLE IF EXISTS events CASCADE');
    
    // Create new events table
    console.log('Creating new events table...');
    await pool.query(`
      CREATE TABLE events (
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
    
    console.log('Table created successfully');
    
    // Now test with PostgresEventStore
    const eventStore = new PostgresEventStore(pool);
    
    const testEvent = {
      id: crypto.randomUUID(),
      payload: { test: 'data' },
      sortableUniqueId: `${new Date().toISOString()}-${crypto.randomUUID()}`,
      version: 1,
      aggregateId: crypto.randomUUID(),
      partitionKeys: {
        rootPartitionKey: 'test',
        group: 'Test',
        aggregateId: crypto.randomUUID(),
        partitionKey: 'test@aggregate@Test'
      },
      timestamp: new Date(),
      partitionKey: 'test@aggregate@Test',
      aggregateGroup: 'Test',
      aggregateType: 'Test',
      eventType: 'TestEvent',
      metadata: {
        causationId: 'test-cause',
        correlationId: 'test-corr',
        executedUser: 'test-user',
        userId: 'test-user',
        timestamp: new Date()
      }
    };

    console.log('Saving test event...');
    console.log('EventStore type:', eventStore.constructor.name);
    console.log('saveEvents method:', typeof eventStore.saveEvents);
    
    const saveResult = eventStore.saveEvents([testEvent]);
    console.log('Result type:', saveResult.constructor.name);
    
    await saveResult;
    console.log('Event saved successfully!');
    
    // Query to verify
    const queryResult = await pool.query('SELECT * FROM events LIMIT 1');
    console.log('Event in database:', queryResult.rows[0]);

  } catch (error) {
    console.error('Error:', error);
  } finally {
    await pool.end();
  }
}

testWithNewTable().catch(console.error);