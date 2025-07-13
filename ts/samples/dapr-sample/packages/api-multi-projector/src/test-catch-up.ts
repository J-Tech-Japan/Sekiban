import { InMemoryEventStore, SortableUniqueId, Event, PartitionKeys } from '@sekiban/core';
import { getDaprCradle, registerMultiProjectorActor, MultiProjectorActorFactory } from '@sekiban/dapr';
import { createSchemaDomainTypes } from '@dapr-sample/domain';
import { DaprClient, DaprServer, HttpMethod } from '@dapr/dapr';

console.log('🧪 Testing Multi-Projector Catch-up from Event Store\n');

// Initialize event store and add events manually
const eventStore = new InMemoryEventStore();
const domainTypes = createSchemaDomainTypes();

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
    partitionKeys: PartitionKeys.generate('Task', 'task-1').value,
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
    partitionKeys: PartitionKeys.generate('Task', 'task-2').value,
    metadata: {}
  }
];

// Store events in the event store
async function prepareEventStore() {
  console.log('📝 Adding events to event store...');
  for (const eventData of events) {
    const event = new Event(
      SortableUniqueId.fromString(eventData.sortableUniqueId).value!,
      eventData.aggregateType,
      eventData.aggregateId,
      eventData.type,
      eventData.payload,
      eventData.version,
      eventData.partitionKeys,
      eventData.metadata
    );
    await eventStore.appendEvents([event]);
  }
  console.log(`✅ Added ${events.length} events to event store\n`);
}

// Test catch-up behavior
async function testCatchUp() {
  await prepareEventStore();
  
  // Initialize Dapr container with event store
  const daprCradle = getDaprCradle({
    domainTypes,
    eventStore
  });
  
  const daprClient = new DaprClient({ daprHost: '127.0.0.1', daprPort: '3503' });
  
  console.log('🔍 Testing actor activation and catch-up...\n');
  
  // Create actor proxy
  const projectorName = 'taskprojector';
  const actorType = 'MultiProjectorActor';
  const actorId = `aggregatelistprojector-${projectorName}`;
  
  console.log(`📤 Invoking actor ${actorId} to trigger activation...\n`);
  
  try {
    // Invoke queryListAsync to trigger actor activation
    const result = await daprClient.actor.invoke(
      actorType,
      actorId,
      'queryListAsync',
      {
        queryType: 'GetAllTasks',
        payload: {},
        skip: 0,
        take: 10
      }
    );
    
    console.log('📊 Query result after catch-up:');
    console.log(JSON.stringify(result, null, 2));
    
    if (result && result.isSuccess && result.items) {
      console.log(`\n✅ Catch-up successful! Found ${result.items.length} projections`);
      result.items.forEach((item: any, index: number) => {
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