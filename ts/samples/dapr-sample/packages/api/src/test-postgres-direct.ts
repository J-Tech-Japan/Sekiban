import { PostgresEventStore } from '@sekiban/postgres';
import { Pool } from 'pg';

async function testPostgresDirect() {
  const pool = new Pool({
    host: 'localhost',
    port: 5432,
    database: 'sekiban_events',
    user: 'sekiban',
    password: 'sekiban_password'
  });

  const eventStore = new PostgresEventStore(pool);

  try {
    // Initialize the database
    console.log('Initializing database...');
    const initResult = await eventStore.initialize();
    if (initResult.isErr()) {
      console.error('Failed to initialize:', initResult.error);
      return;
    }
    console.log('Database initialized successfully');

    // Try to save a simple event
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

    console.log('Saving test event:', JSON.stringify(testEvent, null, 2));
    try {
      await eventStore.saveEvents([testEvent]);
      console.log('Event saved successfully!');
    } catch (saveError) {
      console.error('Failed to save:', saveError);
    }

  } catch (error) {
    console.error('Unexpected error:', error);
  } finally {
    await pool.end();
  }
}

testPostgresDirect().catch(console.error);