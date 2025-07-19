import { InMemoryEventStore, StorageProviderType, SortableUniqueId, PartitionKeys } from '@sekiban/core';
import { initializeDaprContainer } from '@sekiban/dapr';
import { createTaskDomainTypes } from '@dapr-sample/domain';
import { DaprClient, HttpMethod } from '@dapr/dapr';
import type { EventStoreWithSaveEvents } from './types/test-types.js';

interface SerializableListQuery {
  queryType: string;
  payload: any;
  skip: number;
  take: number;
}

interface SerializableQuery {
  queryType: string;
  payload: any;
}

console.log('🧪 Testing Complete MultiProjectorActor Flow\n');

async function testCompleteFlow() {
  // Create in-memory event store
  const eventStore = new InMemoryEventStore({ 
    type: StorageProviderType.InMemory,
    enableLogging: false 
  });
  
  // Initialize domain types
  const domainTypes = createTaskDomainTypes();
  
  // Add test events to store
  console.log('📝 Creating test events...');
  const events = [];
  for (let i = 1; i <= 3; i++) {
    const taskId = `task-${i}`;
    const partitionKeys = PartitionKeys.existing(taskId, 'Task');
    
    // Create an event object that matches IEvent interface
    const event = {
      id: SortableUniqueId.create(),
      partitionKeys,
      aggregateType: 'Task',
      eventType: 'TaskCreated',
      version: 1,
      payload: {
        taskId,
        title: `Test Task ${i}`,
        description: `Description for task ${i}`,
        priority: i === 1 ? 'high' : 'medium',
        createdAt: new Date().toISOString()
      },
      metadata: { timestamp: new Date() },
      // Additional fields for IEvent interface
      aggregateId: taskId,
      sortableUniqueId: SortableUniqueId.create(),
      timestamp: new Date(),
      partitionKey: partitionKeys.partitionKey,
      aggregateGroup: 'Task'
    };
    events.push(event);
  }
  
  await (eventStore as EventStoreWithSaveEvents).saveEvents(events);
  console.log(`✅ Added ${events.length} events to store\n`);
  
  // Initialize Dapr container
  initializeDaprContainer({
    domainTypes,
    serviceProvider: {},
    actorProxyFactory: {
      createActorProxy: <T>(actorId: any, actorType: string): T => {
        throw new Error(`Actor proxy creation not supported in test mode for ${actorType}`);
      }
    },
    serializationService: {},
    eventStore
  });
  
  // Create DaprClient to invoke actor methods
  const daprClient = new DaprClient({
    daprHost: '127.0.0.1',
    daprPort: '3500'
  });
  
  const actorId = 'aggregatelistprojector-taskprojector';
  
  console.log('🔍 Testing actor functionality via HTTP...');
  try {
    // Query projections using Dapr HTTP API
    console.log('📊 Querying projections...');
    const listQuery: SerializableListQuery = {
      queryType: 'GetAllTasks',
      payload: {},
      skip: 0,
      take: 10
    };
    
    const result = await daprClient.invoker.invoke(
      'dapr-sample-api-multi-projector',
      `actors/MultiProjectorActor/${actorId}/method/queryListAsync`,
      HttpMethod.PUT,
      listQuery
    );
    
    console.log('Query Result:', JSON.stringify(result, null, 2));
    
    const typedResult = result as { isSuccess?: boolean; items?: any[] };
    if (typedResult.isSuccess && typedResult.items && typedResult.items.length > 0) {
      console.log(`\n✅ SUCCESS! Found ${typedResult.items.length} projections`);
      console.log('\nProjections:');
      typedResult.items.forEach((item, index: number) => {
        const itemPayload = item as { title?: string; payload?: { title?: string; priority?: string }; priority?: string };
        console.log(`  ${index + 1}. ${itemPayload.title || itemPayload.payload?.title} (${itemPayload.priority || itemPayload.payload?.priority})`);
      });
    } else {
      console.log('\n❌ No projections found');
    }
    
    // Test single query
    console.log('\n🔍 Testing single query...');
    const singleQuery: SerializableQuery = {
      queryType: 'GetTaskById',
      payload: { id: 'task-1' }
    };
    
    const singleResult = await daprClient.invoker.invoke(
      'dapr-sample-api-multi-projector',
      `actors/MultiProjectorActor/${actorId}/method/queryAsync`,
      HttpMethod.PUT,
      singleQuery
    );
    
    console.log('Single query result:', JSON.stringify(singleResult, null, 2));
    
  } catch (error) {
    console.error('❌ Error:', error);
  }
}

testCompleteFlow()
  .then(() => console.log('\n✨ Test completed'))
  .catch(console.error);