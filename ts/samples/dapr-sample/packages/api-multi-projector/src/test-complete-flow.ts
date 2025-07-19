import { DaprClient, HttpMethod } from '@dapr/dapr';

async function testCompleteFlow() {
  const daprClient = new DaprClient({ daprHost: '127.0.0.1', daprPort: '3503' });
  
  console.log('🧪 Testing Complete Multi-Projector Flow\n');
  
  // Wait a bit for services to be ready
  await new Promise(resolve => setTimeout(resolve, 3000));
  
  try {
    // 1. Publish an event
    console.log('📤 Publishing TaskCreated event...');
    const taskId = 'test-' + Date.now();
    const event = {
      id: taskId,
      type: 'TaskCreated',
      aggregateType: 'Task',
      aggregateId: taskId,
      payload: {
        taskId,
        title: 'Complete Flow Test Task',
        description: 'Testing the complete flow',
        priority: 'high',
        createdAt: new Date().toISOString()
      },
      version: 1,
      partitionKeys: {
        aggregateId: taskId,
        group: 'Task',
        rootPartitionKey: 'default'
      },
      createdAt: new Date().toISOString()
    };
    
    await daprClient.pubsub.publish('pubsub', 'sekiban-events', event);
    console.log('✅ Event published\n');
    
    // 2. Wait for processing
    console.log('⏳ Waiting for event processing...');
    await new Promise(resolve => setTimeout(resolve, 2000));
    
    // 3. Query the projections
    console.log('🔍 Querying projections...');
    const result = await daprClient.invoker.invoke(
      'dapr-sample-api-multi-projector', // app-id
      'actors/MultiProjectorActor/aggregatelistprojector-taskprojector/method/queryListAsync',
      HttpMethod.PUT,
      {
        queryType: 'GetAllTasks',
        payload: {},
        skip: 0,
        take: 10
      }
    );
    
    console.log('\n📊 Query Result:');
    console.log(JSON.stringify(result, null, 2));
    
    const typedResult = result as { isSuccess?: boolean; items?: any[] };
    if (typedResult && typedResult.isSuccess && typedResult.items && typedResult.items.length > 0) {
      console.log(`\n✅ SUCCESS! Found ${typedResult.items.length} projections`);
      console.log('\nProjections:');
      typedResult.items.forEach((item: any, index: number) => {
        console.log(`  ${index + 1}. ${item.title || item.taskId} (${item.priority || 'N/A'})`);
      });
    } else {
      console.log('\n❌ No projections found');
    }
    
  } catch (error) {
    console.error('❌ Error:', error);
  }
}

testCompleteFlow()
  .then(() => {
    console.log('\n✨ Test completed');
    process.exit(0);
  })
  .catch(err => {
    console.error('💥 Test failed:', err);
    process.exit(1);
  });