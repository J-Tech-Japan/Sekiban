// Test full flow with pub/sub
import { DaprClient, ActorId, ActorProxyBuilder, HttpMethod } from '@dapr/dapr';
import { createTaskDomainTypes } from '@dapr-sample/domain';
import { InMemoryEventStore, StorageProviderType, SortableUniqueId, PartitionKeys } from '@sekiban/core';
import { MultiProjectorActor, MultiProjectorActorFactory, initializeDaprContainer } from '@sekiban/dapr';
import type { IActorProxyFactory } from './types/test-types.js';

console.log('🧪 Testing Full Flow with Pub/Sub\n');

async function sleep(ms: number) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

async function testFullPubSub() {
  // Initialize event store
  const eventStore = new InMemoryEventStore({ 
    type: StorageProviderType.InMemory,
    enableLogging: false 
  });
  
  // Initialize domain types
  const domainTypes = createTaskDomainTypes();
  
  // Create DaprClient
  const daprClient = new DaprClient({
    daprHost: "127.0.0.1",
    daprPort: "3500"  // Default Dapr HTTP port
  });
  
  // Create actor proxy factory
  const actorProxyFactory: IActorProxyFactory = {
    createActorProxy: <T>(actorId: any, actorType: string): T => {
      console.log(`Creating actor proxy for ${actorType}/${actorId.id || actorId}`);
      if (actorType === 'MultiProjectorActor') {
        const factory = MultiProjectorActorFactory as unknown as { createActorClass(): typeof MultiProjectorActor };
        const MultiProjectorActorClass = factory.createActorClass();
        const builder = new ActorProxyBuilder(MultiProjectorActorClass, daprClient);
        return builder.build(new ActorId(actorId.id || actorId)) as T;
      }
      throw new Error(`Unknown actor type: ${actorType}`);
    }
  };
  
  // Create serialization service
  const serializationService = {
    async deserializeAggregateAsync(surrogate: any) {
      return surrogate;
    },
    async serializeAggregateAsync(aggregate: any) {
      return aggregate;
    }
  };
  
  // Initialize container
  initializeDaprContainer({
    domainTypes,
    serviceProvider: {},
    actorProxyFactory,
    serializationService,
    eventStore,
    daprClient,
    configuration: {
      pubSubName: 'pubsub',
      eventTopicName: 'sekiban-events'
    }
  });
  
  console.log('📦 Creating test event...');
  
  // Create a test event in the proper SerializableEventDocument format
  const taskId = 'task-' + Date.now();
  const event = {
    Id: SortableUniqueId.create().value,
    SortableUniqueId: SortableUniqueId.create().value,
    Version: 1,
    
    // Partition keys
    AggregateId: taskId,
    AggregateGroup: 'Task',
    RootPartitionKey: 'default',
    
    // Event info - PayloadTypeName is the event type!
    PayloadTypeName: 'TaskCreated',
    TimeStamp: new Date().toISOString(),
    PartitionKey: `Task-${taskId}`,
    
    // Metadata
    CausationId: '',
    CorrelationId: '',
    ExecutedUser: 'test-user',
    
    // Payload (base64 encoded JSON)
    CompressedPayloadJson: Buffer.from(JSON.stringify({
      taskId: taskId,
      title: 'Test Task via Pub/Sub',
      description: 'This task was created through pub/sub',
      priority: 'high',
      createdAt: new Date().toISOString()
    })).toString('base64'),
    
    // Version
    PayloadAssemblyVersion: '0.0.0.0'
  };
  
  console.log('Event:', {
    PayloadTypeName: event.PayloadTypeName,
    AggregateId: event.AggregateId,
    AggregateGroup: event.AggregateGroup
  });
  
  // Publish event
  console.log('\n📡 Publishing event to pub/sub...');
  try {
    await daprClient.pubsub.publish('pubsub', 'sekiban-events', event);
    console.log('✅ Event published successfully');
  } catch (error) {
    console.error('❌ Failed to publish event:', error);
    return;
  }
  
  // Wait for processing
  console.log('\n⏳ Waiting for event processing...');
  await sleep(2000);
  
  // Query projections
  console.log('\n📊 Querying projections from MultiProjectorActor...');
  
  // Create actor proxy
  const multiProjectorActorId = 'aggregatelistprojector-taskprojector';
  const proxy = actorProxyFactory.createActorProxy(
    new ActorId(multiProjectorActorId),
    'MultiProjectorActor'
  );
  
  if (!proxy) {
    console.error('❌ Failed to create actor proxy');
    return;
  }
  
  try {
    // Use HTTP invoke instead of direct method call
    const result = await daprClient.invoker.invoke(
      'dapr-sample-api-multi-projector',
      `actors/MultiProjectorActor/${multiProjectorActorId}/method/queryListAsync`,
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
      console.log(`\n✅ Found ${typedResult.items.length} projections`);
      const foundOurTask = typedResult.items.find((item: any) => item.id === taskId);
      
      if (foundOurTask) {
        console.log('\n✅ SUCCESS! Found our task in projections:');
        console.log('  ID:', foundOurTask.id);
        console.log('  Payload:', JSON.stringify(foundOurTask.payload, null, 2));
        
        if (foundOurTask.payload && !foundOurTask.payload._empty) {
          console.log('\n🎉 COMPLETE SUCCESS! Projection has actual data!');
        }
      } else {
        console.log('\n⚠️ Task not found in projections yet');
      }
    } else {
      console.log('\n❌ No projections found');
    }
  } catch (error) {
    console.error('❌ Error querying projections:', error);
  }
}

// Check if we're running directly
if (import.meta.url === `file://${process.argv[1]}`) {
  testFullPubSub()
    .then(() => {
      console.log('\n✨ Test completed');
      process.exit(0);
    })
    .catch(error => {
      console.error('❌ Test failed:', error);
      process.exit(1);
    });
}