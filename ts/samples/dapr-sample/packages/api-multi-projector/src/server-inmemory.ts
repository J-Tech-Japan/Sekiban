import express from 'express';
import { DaprServer, DaprClient, CommunicationProtocolEnum } from '@dapr/dapr';
import { MultiProjectorActorFactory, initializeDaprContainer } from '@sekiban/dapr';
import { InMemoryEventStore, StorageProviderType } from '@sekiban/core';
import { createTaskDomainTypes } from '@dapr-sample/domain';
import dotenv from 'dotenv';

dotenv.config();

console.log('Starting Multi-Projector Service with In-Memory Event Store...');

// Initialize in-memory event store
const eventStore = new InMemoryEventStore({ 
  type: StorageProviderType.InMemory,
  enableLogging: true 
});
console.log('Using in-memory event store');

// Initialize domain types
const domainTypes = createTaskDomainTypes();

// Initialize Dapr container with dependencies
initializeDaprContainer({
  domainTypes,
  serviceProvider: {},
  actorProxyFactory: {} as any,
  serializationService: {},
  eventStore
});

const daprHost = process.env.DAPR_HOST || '127.0.0.1';
const daprPort = process.env.DAPR_HTTP_PORT || '3513';
const serverPort = process.env.PORT || '3015';

// Create DaprClient for actor proxy factory
const daprClient = new DaprClient({
  daprHost,
  daprPort: String(daprPort),
  communicationProtocol: CommunicationProtocolEnum.HTTP
});

// Configure factory with dependencies
MultiProjectorActorFactory.configure(
  domainTypes,
  {},
  daprClient,
  {},
  eventStore
);

// Create actor class
const MultiProjectorActorClass = MultiProjectorActorFactory.createActorClass();

// Create DaprServer
const daprServer = new DaprServer({
  serverHost: '127.0.0.1',
  serverPort: String(serverPort),
  communicationProtocol: CommunicationProtocolEnum.HTTP,
  clientOptions: {
    daprHost,
    daprPort: String(daprPort),
    communicationProtocol: CommunicationProtocolEnum.HTTP
  }
});

// Register MultiProjectorActor
await daprServer.actor.registerActor(MultiProjectorActorClass);
console.log('Registered MultiProjectorActor');

// Create Express app for additional endpoints
const app = express();
app.use(express.json());

// Test endpoint to populate some events
app.post('/test/populate-events', async (req, res) => {
  console.log('Populating test events...');
  
  try {
    // Import required classes
    const { Event, SortableUniqueId, PartitionKeys } = await import('@sekiban/core');
    
    // Add some test events to the store
    const events = [];
    for (let i = 1; i <= 5; i++) {
      const taskId = `task-${i}`;
      const partitionKeys = PartitionKeys.existing(taskId, 'Task');
      const event = new Event(
        SortableUniqueId.create(),
        partitionKeys,
        'Task',
        'TaskCreated',
        1,
        {
          taskId,
          title: `Test Task ${i}`,
          description: `Description for task ${i}`,
          priority: i % 2 === 0 ? 'high' : 'medium',
          createdAt: new Date().toISOString()
        },
        { timestamp: new Date() }
      );
      events.push(event);
    }
    
    await (eventStore as any).saveEvents(events);
    console.log(`Added ${events.length} test events to store`);
    
    res.json({ success: true, eventsAdded: events.length });
  } catch (error) {
    console.error('Error populating events:', error);
    res.status(500).json({ error: error.message });
  }
});

// Query endpoint for testing
app.post('/test/query-projections', async (req, res) => {
  try {
    const daprClient = new DaprClient({ daprHost, daprPort });
    const result = await daprClient.actor.invoke(
      'MultiProjectorActor',
      'aggregatelistprojector-taskprojector',
      'queryListAsync',
      {
        queryType: 'GetAllTasks',
        payload: {},
        skip: 0,
        take: 10
      }
    );
    
    res.json(result);
  } catch (error) {
    console.error('Error querying projections:', error);
    res.status(500).json({ error: error.message });
  }
});

// Setup pub/sub subscription
console.log('Setting up pub/sub subscription...');
await daprServer.pubsub.subscribe('pubsub', 'sekiban-events', async (data: any) => {
  console.log('[PubSub] Received event:', {
    type: data?.type,
    aggregateType: data?.aggregateType,
    aggregateId: data?.aggregateId
  });
  
  try {
    // Distribute to MultiProjectorActors
    const daprClient = new DaprClient({ daprHost, daprPort });
    const projectorName = 'taskprojector';
    const actorId = `aggregatelistprojector-${projectorName}`;
    
    await daprClient.actor.invoke(
      'MultiProjectorActor',
      actorId,
      'receiveEventAsync',
      data
    );
    
    console.log(`[PubSub] Event distributed to ${actorId}`);
  } catch (error) {
    console.error('[PubSub] Error processing event:', error);
    throw error;
  }
});

// Initialize actor runtime
console.log('Initializing actor runtime...');
await daprServer.actor.init();
console.log('Actor runtime initialized');

// Start the DaprServer (it handles its own HTTP server)
await daprServer.start();

// Start Express app on a different port for test endpoints
const testPort = parseInt(serverPort) + 1;
app.listen(testPort, () => {
  console.log(`
🚀 Multi-Projector Service is running!
📡 Using in-memory event store
🔗 Dapr server: http://localhost:${serverPort}
🔗 Test endpoints: http://localhost:${testPort}
🎭 Dapr HTTP port: ${daprPort}
🎭 Actors: MultiProjectorActor
📬 Subscribed to: sekiban-events

Test endpoints:
  POST http://localhost:${testPort}/test/populate-events - Add test events
  POST http://localhost:${testPort}/test/query-projections - Query projections
  `);
});