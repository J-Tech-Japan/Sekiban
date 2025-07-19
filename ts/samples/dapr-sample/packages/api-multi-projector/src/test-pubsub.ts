import { DaprClient, HttpMethod } from '@dapr/dapr';
import { v4 as uuidv4 } from 'uuid';
import { SortableUniqueId } from '@sekiban/core';

async function publishTestEvent() {
  console.log('🚀 Testing Dapr Pub/Sub Integration\n');

  const daprClient = new DaprClient({
    daprHost: "127.0.0.1",
    daprPort: "3513" // Multi-projector's Dapr port
  });

  try {
    // Create a test event
    const taskId = uuidv4();
    const event = {
      id: SortableUniqueId.create().value,
      type: 'TaskCreated',
      aggregateType: 'Task',
      aggregateId: taskId,
      payload: {
        taskId,
        title: `Test Task via Pub/Sub ${new Date().toISOString()}`,
        description: 'Testing immediate projection via Dapr pub/sub',
        assigneeEmail: 'pubsub@example.com'
      },
      version: 1,
      partitionKeys: {
        aggregateId: taskId,
        group: 'Task',
        rootPartitionKey: 'default'
      },
      createdAt: new Date().toISOString()
    };

    console.log('📤 Publishing event:', {
      type: event.type,
      aggregateId: event.aggregateId,
      title: event.payload.title
    });

    // Publish to Dapr pub/sub
    await daprClient.pubsub.publish(
      'pubsub',           // pubsub component name
      'sekiban-events',   // topic name
      event              // event data
    );

    console.log('✅ Event published successfully!');

    // Wait a bit for processing
    console.log('\n⏳ Waiting for event to be processed...');
    await new Promise(resolve => setTimeout(resolve, 2000));

    // Query the MultiProjectorActor to see if event was received
    console.log('\n🔍 Checking if event was processed...');
    
    const actorId = 'aggregatelistprojector-taskprojector';
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

    console.log('\n📊 Query result:', JSON.stringify(queryResponse, null, 2));

    // Publish another event type
    console.log('\n📤 Publishing TaskAssigned event...');
    
    const assignEvent = {
      id: SortableUniqueId.create().value,
      type: 'TaskAssigned',
      aggregateType: 'Task',
      aggregateId: taskId,
      payload: {
        taskId,
        assigneeEmail: 'developer@pubsub.com',
        assignedAt: new Date().toISOString()
      },
      version: 2,
      partitionKeys: {
        aggregateId: taskId,
        group: 'Task',
        rootPartitionKey: 'default'
      },
      createdAt: new Date().toISOString()
    };

    await daprClient.pubsub.publish('pubsub', 'sekiban-events', assignEvent);
    console.log('✅ TaskAssigned event published!');

    // Test bulk publish
    console.log('\n📤 Publishing multiple events in bulk...');
    
    const bulkEvents = [];
    for (let i = 0; i < 3; i++) {
      const bulkTaskId = uuidv4();
      bulkEvents.push({
        id: SortableUniqueId.create().value,
        type: 'TaskCreated',
        aggregateType: 'Task',
        aggregateId: bulkTaskId,
        payload: {
          taskId: bulkTaskId,
          title: `Bulk Task ${i + 1}`,
          description: `Bulk test task number ${i + 1}`,
          assigneeEmail: 'bulk@example.com'
        },
        version: 1,
        partitionKeys: {
          aggregateId: bulkTaskId,
          group: 'Task',
          rootPartitionKey: 'default'
        },
        createdAt: new Date().toISOString()
      });
    }

    // Publish each event
    for (const evt of bulkEvents) {
      await daprClient.pubsub.publish('pubsub', 'sekiban-events', evt);
      console.log(`✅ Published bulk event: ${evt.payload.title}`);
    }

    // Wait and check final state
    await new Promise(resolve => setTimeout(resolve, 3000));

    console.log('\n🔍 Final query to check all events...');
    const finalResponse = await daprClient.invoker.invoke(
      'dapr-sample-api-multi-projector',
      `actors/MultiProjectorActor/${actorId}/method/queryListAsync`,
      HttpMethod.PUT,
      [{
        queryType: 'TaskProjector',
        payload: {},
        skip: 0,
        take: 20
      }]
    );

    console.log('\n📊 Final result:', JSON.stringify(finalResponse, null, 2));

    console.log('\n✅ Pub/Sub test completed successfully!');

  } catch (error) {
    console.error('❌ Error:', error);
  }
}

// Run the test
publishTestEvent().catch(console.error);