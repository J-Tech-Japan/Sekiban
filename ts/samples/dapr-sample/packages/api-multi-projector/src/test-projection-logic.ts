import { InMemoryEventStore, StorageProviderType, SortableUniqueId, IEvent, PartitionKeys, createEvent, createEventMetadata } from '@sekiban/core';
import { initializeDaprContainer } from '@sekiban/dapr';
import { createTaskDomainTypes } from '@dapr-sample/domain';
import { DaprClient, HttpMethod } from '@dapr/dapr';
import type { EventStoreWithSaveEvents } from './types/test-types.js';

console.log('🧪 Testing MultiProjectorActor Projection Logic\n');

async function testProjectionLogic() {
  // Create in-memory event store
  const eventStore = new InMemoryEventStore({ type: StorageProviderType.InMemory });
  const domainTypes = createTaskDomainTypes();
  
  // Add test event to store
  const taskId = 'task-test-1';
  const partitionKeys = PartitionKeys.existing(taskId, 'Task');
  const event = createEvent({
    id: SortableUniqueId.create(),
    partitionKeys,
    aggregateType: 'Task',
    eventType: 'TaskCreated',
    version: 1,
    payload: {
      taskId,
      title: 'Test Task for Projection',
      description: 'Testing projection logic',
      priority: 'high',
      createdAt: new Date().toISOString()
    },
    metadata: createEventMetadata({ timestamp: new Date() })
  });
  
  await (eventStore as EventStoreWithSaveEvents).saveEvents([event]);
  console.log('✅ Added test event to store\n');
  
  // Initialize Dapr container
  initializeDaprContainer({
    domainTypes,
    serviceProvider: {},
    actorProxyFactory: {},
    serializationService: {},
    eventStore
  });
  
  // Create DaprClient to invoke actor methods
  const daprClient = new DaprClient({
    daprHost: '127.0.0.1',
    daprPort: '3500'
  });
  
  const actorId = 'aggregatelistprojector-taskprojector';
  
  console.log('🔍 Testing MultiProjectorActor via HTTP...');
  try {
    // First, wait a bit to ensure actor is ready
    console.log('⏳ Waiting for actor to be ready...');
    await new Promise(resolve => setTimeout(resolve, 2000));
    
    // Query current state
    console.log('📊 Querying projections...');
    const result = await daprClient.invoker.invoke(
      'dapr-sample-api-multi-projector',
      `actors/MultiProjectorActor/${actorId}/method/queryListAsync`,
      HttpMethod.PUT,
      {
        queryType: 'GetAllTasks',
        payload: {},
        skip: 0,
        take: 10
      }
    );
    
    console.log('Query Result:', JSON.stringify(result, null, 2));
    
    const typedResult = result as { isSuccess?: boolean; items?: any[] };
    if (typedResult.isSuccess && typedResult.items && typedResult.items.length > 0) {
      console.log(`\n✅ SUCCESS! Found ${typedResult.items.length} projections`);
    } else {
      console.log('\n❌ No projections found');
      console.log('Note: This test requires the api-multi-projector service to be running');
    }
    
  } catch (error) {
    console.error('❌ Error:', error);
    console.log('\nNote: Make sure the api-multi-projector service is running with Dapr');
  }
}

testProjectionLogic().catch(console.error);