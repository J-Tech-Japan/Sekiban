import { InMemoryEventStore, StorageProviderType, SortableUniqueId, IEvent, PartitionKeys, createEvent, createEventMetadata } from '@sekiban/core';
import { MultiProjectorActor, initializeDaprContainer } from '@sekiban/dapr';
import { createTaskDomainTypes } from '@dapr-sample/domain';
import { DaprClient, ActorId } from '@dapr/dapr';

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
  
  await (eventStore as any).saveEvents([event]);
  console.log('✅ Added test event to store\n');
  
  // Initialize Dapr container
  initializeDaprContainer({
    domainTypes,
    serviceProvider: {},
    actorProxyFactory: {} as any,
    serializationService: {},
    eventStore
  });
  
  // Mock Dapr client with proper state management
  const actorState: any = {};
  const mockDaprClient = {
    actor: {
      getState: async (key: string) => {
        return [actorState.hasOwnProperty(key), actorState[key]];
      },
      saveState: async (states: any[]) => {
        for (const state of states) {
          actorState[state.key] = state.value;
        }
      },
      registerReminder: async () => {},
      unregisterReminder: async () => {}
    }
  } as any;
  
  // Create MultiProjectorActor instance
  const actorId = new ActorId('aggregatelistprojector-taskprojector');
  const actor = new MultiProjectorActor(mockDaprClient, actorId);
  
  // Mock getStateManager
  (actor as any).getStateManager = async () => ({
    tryGetState: async (key: string) => {
      return [actorState.hasOwnProperty(key), actorState[key]];
    },
    setState: async (key: string, value: any) => {
      actorState[key] = value;
    }
  });
  
  // Test catch-up from store
  console.log('🔍 Testing catchUpFromStoreAsync...');
  try {
    // Call onActivate which should trigger catch-up
    await actor.onActivate();
    console.log('✅ onActivate completed\n');
    
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
    } else {
      console.log('\n❌ No projections found');
    }
    
  } catch (error) {
    console.error('❌ Error:', error);
  }
}

testProjectionLogic().catch(console.error);