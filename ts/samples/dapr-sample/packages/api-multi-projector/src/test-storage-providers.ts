import { DaprClient, HttpMethod } from '@dapr/dapr';
import { InMemoryEventStore as StorageInMemoryEventStore, StorageProviderType, PartitionKeys, IEvent, SortableUniqueId, createEvent, createEventMetadata } from '@sekiban/core';
import { PostgresEventStore } from '@sekiban/postgres';
import { createTaskDomainTypes } from '@dapr-sample/domain';
import pg from 'pg';
import { v4 as uuidv4 } from 'uuid';

const { Pool } = pg;

async function testWithInMemory() {
  console.log('🧪 Testing with InMemory Event Store...\n');
  
  // Use the already imported InMemoryEventStore
  const InMemoryEventStore = StorageInMemoryEventStore;
  
  // Create InMemory event store
  const eventStore = new InMemoryEventStore({
    type: StorageProviderType.InMemory,
    enableLogging: true
  });
  
  // Initialize
  const initResult = await eventStore.initialize();
  if (initResult.isErr()) {
    console.error('Failed to initialize InMemory store:', initResult.error);
    return;
  }
  
  // Add some test events
  const taskId = uuidv4();
  const partitionKeys = PartitionKeys.existing(taskId, 'Task');
  
  // Create proper IEvent objects
  const events: IEvent[] = [
    {
      id: SortableUniqueId.create(),
      aggregateType: 'Task',
      aggregateId: taskId,
      type: 'TaskCreated',
      payload: {
        taskId,
        title: 'Test Task InMemory',
        description: 'Testing with InMemory storage',
        assigneeEmail: 'test@example.com'
      },
      version: 1,
      partitionKeys,
      sortableUniqueId: SortableUniqueId.create(),
      createdAt: new Date()
    },
    {
      id: SortableUniqueId.create(),
      aggregateType: 'Task',
      aggregateId: taskId,
      type: 'TaskAssigned',
      payload: {
        taskId,
        assigneeEmail: 'developer@example.com'
      },
      version: 2,
      partitionKeys,
      sortableUniqueId: SortableUniqueId.create(),
      createdAt: new Date()
    }
  ];
  
  // Use saveEvents method
  try {
    await eventStore.saveEvents(events);
    console.log('✅ Added test events to InMemory store');
  } catch (error) {
    console.error('Failed to save events:', error);
    return;
  }
  
  // Query through MultiProjectorActor
  await queryMultiProjector('InMemory');
}

async function testWithPostgres() {
  console.log('\n🧪 Testing with PostgreSQL Event Store...\n');
  
  // Create PostgreSQL event store
  const pool = new Pool({
    connectionString: 'postgresql://sekiban:sekiban_password@localhost:5432/sekiban_events'
  });
  
  const eventStore = new PostgresEventStore(pool);
  
  // Initialize
  const initResult = await eventStore.initialize();
  if (initResult.isErr()) {
    console.error('Failed to initialize PostgreSQL store:', initResult.error);
    await pool.end();
    return;
  }
  
  console.log('✅ PostgreSQL schema initialized');
  
  // Add some test events
  const taskId = uuidv4();
  const partitionKeys = PartitionKeys.existing(taskId, 'Task');
  
  // Create proper IEvent objects
  const events: IEvent[] = [
    {
      id: SortableUniqueId.create(),
      aggregateType: 'Task',
      aggregateId: taskId,
      type: 'TaskCreated',
      payload: {
        taskId,
        title: 'Test Task PostgreSQL',
        description: 'Testing with PostgreSQL storage',
        assigneeEmail: 'postgres@example.com'
      },
      version: 1,
      partitionKeys,
      sortableUniqueId: SortableUniqueId.create(),
      createdAt: new Date()
    },
    {
      id: SortableUniqueId.create(),
      aggregateType: 'Task',
      aggregateId: taskId,
      type: 'TaskCompleted',
      payload: {
        taskId,
        completedAt: new Date().toISOString()
      },
      version: 2,
      partitionKeys,
      sortableUniqueId: SortableUniqueId.create(),
      createdAt: new Date()
    }
  ];
  
  // Use saveEvents method
  try {
    await eventStore.saveEvents(events);
    console.log('✅ Added test events to PostgreSQL store');
  } catch (error) {
    console.error('Failed to save events:', error);
    await pool.end();
    return;
  }
  
  // Query through MultiProjectorActor
  await queryMultiProjector('PostgreSQL');
  
  // Clean up
  await pool.end();
}

async function queryMultiProjector(storageType: string) {
  console.log(`\n📊 Querying MultiProjectorActor (${storageType})...`);
  
  const daprClient = new DaprClient({
    daprHost: "127.0.0.1",
    daprPort: "3513"
  });
  
  try {
    const actorId = 'aggregatelistprojector-taskprojector';
    
    // Force rebuild state to pick up new events
    console.log('📋 Rebuilding state...');
    const buildResponse = await daprClient.invoker.invoke(
      'dapr-sample-api-multi-projector',
      `actors/MultiProjectorActor/${actorId}/method/buildStateAsync`,
      HttpMethod.PUT,
      []
    );
    
    console.log('Build response:', JSON.stringify(buildResponse, null, 2));
    
    // Query list
    console.log('\n📋 Querying projections...');
    const queryResponse = await daprClient.invoker.invoke(
      'dapr-sample-api-multi-projector',
      `actors/MultiProjectorActor/${actorId}/method/queryListAsync`,
      HttpMethod.PUT,
      [{
        queryType: 'TaskProjector',
        payload: {},
        skip: 0,
        take: 10
      }]
    );
    
    console.log('Query response:', JSON.stringify(queryResponse, null, 2));
    
  } catch (error) {
    console.error('❌ Error querying MultiProjectorActor:', error);
  }
}

async function runTests() {
  console.log('🚀 Starting Storage Provider Tests\n');
  
  // Test with InMemory
  await testWithInMemory();
  
  // Wait a bit between tests
  await new Promise(resolve => setTimeout(resolve, 2000));
  
  // Test with PostgreSQL
  await testWithPostgres();
  
  console.log('\n✅ All tests completed!');
}

// Check if PostgreSQL is available
async function checkPostgresConnection(): Promise<boolean> {
  const pool = new Pool({
    connectionString: 'postgresql://sekiban:sekiban_password@localhost:5432/sekiban_events'
  });
  
  try {
    await pool.query('SELECT 1');
    await pool.end();
    return true;
  } catch (error) {
    console.warn('⚠️  PostgreSQL is not available, skipping PostgreSQL tests');
    await pool.end();
    return false;
  }
}

// Run the tests
(async () => {
  const postgresAvailable = await checkPostgresConnection();
  
  if (postgresAvailable) {
    await runTests();
  } else {
    console.log('Running InMemory tests only...\n');
    await testWithInMemory();
  }
})().catch(console.error);