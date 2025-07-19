import { InMemoryEventStore, StorageProviderType, SortableUniqueId, IEvent, PartitionKeys, createEvent, createEventMetadata } from '@sekiban/core';
import { getDaprCradle, MultiProjectorActorFactory } from '@sekiban/dapr';
import { createTaskDomainTypes } from '@dapr-sample/domain';
import { DaprClient, DaprServer, HttpMethod, ActorId } from '@dapr/dapr';

console.log('🧪 Testing Multi-Projector Catch-up from Event Store\n');

// Initialize event store and add events manually
const eventStore = new InMemoryEventStore({ type: StorageProviderType.InMemory });
const domainTypes = createTaskDomainTypes();

// Create test events
const events = [
  {
    id: SortableUniqueId.create().value,
    sortableUniqueId: SortableUniqueId.create().value,
    aggregateId: 'task-1',
    aggregateType: 'Task',
    type: 'TaskCreated',
    payload: {
      taskId: 'task-1',
      title: 'Test Task 1',
      description: 'Created via catch-up test',
      priority: 'high',
      createdAt: new Date().toISOString()
    },
    version: 1,
    partitionKeys: PartitionKeys.generate('Task', 'task-1'),
    metadata: {}
  },
  {
    id: SortableUniqueId.create().value,
    sortableUniqueId: SortableUniqueId.create().value,
    aggregateId: 'task-2',
    aggregateType: 'Task',
    type: 'TaskCreated',
    payload: {
      taskId: 'task-2',
      title: 'Test Task 2',
      description: 'Another task for catch-up',
      priority: 'medium',
      createdAt: new Date().toISOString()
    },
    version: 1,
    partitionKeys: PartitionKeys.generate('Task', 'task-2'),
    metadata: {}
  }
];

// Store events in the event store
async function prepareEventStore() {
  console.log('📝 Adding events to event store...');
  for (const eventData of events) {
    const event = createEvent({
      id: SortableUniqueId.generate(),
      partitionKeys: eventData.partitionKeys,
      aggregateType: eventData.aggregateType,
      eventType: eventData.type,
      version: eventData.version,
      payload: eventData.payload,
      metadata: createEventMetadata(eventData.metadata)
    });
    await eventStore.saveEvents([event]);
  }
  console.log(`✅ Added ${events.length} events to event store\n`);
}

// Test catch-up behavior
async function testCatchUp() {
  await prepareEventStore();
  
  // Initialize Dapr container with event store
  const daprCradle = getDaprCradle();
  
  const daprClient = new DaprClient({ daprHost: '127.0.0.1', daprPort: '3503' });
  
  console.log('🔍 Testing actor activation and catch-up...\n');
  
  // Create actor proxy
  const projectorName = 'taskprojector';
  const actorType = 'MultiProjectorActor';
  const actorId = `aggregatelistprojector-${projectorName}`;
  
  console.log(`📤 Invoking actor ${actorId} to trigger activation...\n`);
  
  try {
    // Invoke queryListAsync to trigger actor activation
    const result = await daprClient.invoker.invoke(
      'dapr-sample-api-multi-projector', // app-id
      `actors/${actorType}/${actorId}/method/queryListAsync`,
      HttpMethod.PUT,
      {
        queryType: 'GetAllTasks',
        payload: {},
        skip: 0,
        take: 10
      }
    );
    
    console.log('📊 Query result after catch-up:');
    console.log(JSON.stringify(result, null, 2));
    
    const typedResult = result as { isSuccess?: boolean; items?: any[] };
    if (typedResult && typedResult.isSuccess && typedResult.items) {
      console.log(`\n✅ Catch-up successful! Found ${typedResult.items.length} projections`);
      typedResult.items.forEach((item: any, index: number) => {
        console.log(`  ${index + 1}. ${item.title} (${item.priority})`);
      });
    } else {
      console.log('\n❌ No projections found - catch-up may have failed');
    }
  } catch (error) {
    console.error('❌ Error invoking actor:', error);
  }
}

testCatchUp().catch(console.error);