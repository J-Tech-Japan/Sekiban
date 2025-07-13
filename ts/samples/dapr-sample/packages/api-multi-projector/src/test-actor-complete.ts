import { InMemoryEventStore, StorageProviderType, SortableUniqueId, PartitionKeys } from '@sekiban/core';
import { MultiProjectorActor, initializeDaprContainer } from '@sekiban/dapr';
import { createTaskDomainTypes } from '@dapr-sample/domain';
import { ActorId } from '@dapr/dapr';

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
  
  await (eventStore as any).saveEvents(events);
  console.log(`✅ Added ${events.length} events to store\n`);
  
  // Initialize Dapr container
  initializeDaprContainer({
    domainTypes,
    serviceProvider: {},
    actorProxyFactory: {} as any,
    serializationService: {},
    eventStore
  });
  
  // Mock actor infrastructure
  const actorState: any = {};
  const mockStateManager = {
    tryGetState: async (key: string) => {
      return [actorState.hasOwnProperty(key), actorState[key]];
    },
    setState: async (key: string, value: any) => {
      actorState[key] = value;
    }
  };
  
  // Create a minimal mock DaprClient that satisfies AbstractActor requirements
  const mockDaprClient = {
    options: {
      daprHost: '127.0.0.1',
      daprPort: '3500',
      communicationProtocol: 'HTTP',
      isHTTP: true
    },
    actor: {
      registerReminder: async () => {},
      unregisterReminder: async () => {}
    }
  } as any;
  
  // Create MultiProjectorActor instance
  const actorId = new ActorId('aggregatelistprojector-taskprojector');
  const actor = new MultiProjectorActor(mockDaprClient, actorId);
  
  // Override getStateManager method
  (actor as any).getStateManager = async () => mockStateManager;
  
  console.log('🔍 Testing actor onActivate (should trigger catch-up)...');
  try {
    // Call onActivate which should trigger catch-up
    await actor.onActivate();
    console.log('✅ onActivate completed\n');
    
    // Wait a bit for catch-up to process
    await new Promise(resolve => setTimeout(resolve, 100));
    
    // Try to query
    console.log('📊 Querying projections...');
    const result = await actor.queryListAsync({
      queryType: 'GetAllTasks',
      payload: {},
      skip: 0,
      take: 10
    });
    
    console.log('Query Result:', JSON.stringify(result, null, 2));
    
    if (result.isSuccess && result.items && result.items.length > 0) {
      console.log(`\n✅ SUCCESS! Found ${result.items.length} projections`);
      console.log('\nProjections:');
      result.items.forEach((item: any, index: number) => {
        console.log(`  ${index + 1}. ${item.title || item.payload?.title} (${item.priority || item.payload?.priority})`);
      });
    } else {
      console.log('\n❌ No projections found');
      console.log('Actor state:', actorState);
    }
    
    // Test single query
    console.log('\n🔍 Testing single query...');
    const singleResult = await actor.queryAsync({
      queryType: 'GetTaskById',
      payload: { id: 'task-1' }
    });
    
    console.log('Single query result:', JSON.stringify(singleResult, null, 2));
    
  } catch (error) {
    console.error('❌ Error:', error);
  } finally {
    // Clean up
    await actor.onDeactivate();
  }
}

testCompleteFlow()
  .then(() => console.log('\n✨ Test completed'))
  .catch(console.error);