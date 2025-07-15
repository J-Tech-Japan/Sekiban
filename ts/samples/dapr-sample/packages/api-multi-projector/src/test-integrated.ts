import { DaprClient, HttpMethod } from '@dapr/dapr';
import { v4 as uuidv4 } from 'uuid';

async function createTestEvent() {
  console.log('🌱 Creating test event through the main API...\n');

  // Make sure the main API is running
  const daprClient = new DaprClient({
    daprHost: "127.0.0.1",
    daprPort: "3501" // Main API's Dapr port
  });

  try {
    // Check if API is healthy
    const health = await fetch('http://localhost:3001/health');
    if (!health.ok) {
      console.error('❌ Main API is not running. Please start it first.');
      return;
    }
    
    const taskId = uuidv4();
    console.log(`Creating task with ID: ${taskId}`);
    
    // Create a task through the main API
    const createResponse = await fetch('http://localhost:3001/api/v1/tasks', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        taskId,
        title: `Test Task ${new Date().toISOString()}`,
        description: 'Testing MultiProjectorActor with real events',
        assigneeEmail: 'test@example.com'
      })
    });

    if (!createResponse.ok) {
      console.error('Failed to create task:', await createResponse.text());
      return;
    }

    const createdTask = await createResponse.json();
    console.log('✅ Task created:', createdTask);

    // Assign the task
    const assignResponse = await fetch(`http://localhost:3001/api/v1/tasks/${taskId}/assign`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        assigneeEmail: 'developer@example.com'
      })
    });

    if (!assignResponse.ok) {
      console.error('Failed to assign task:', await assignResponse.text());
      return;
    }

    console.log('✅ Task assigned to developer@example.com');

    // Wait for events to propagate
    console.log('\n⏳ Waiting for events to propagate...');
    await new Promise(resolve => setTimeout(resolve, 3000));

    // Now query the MultiProjectorActor
    await queryMultiProjector();

  } catch (error) {
    console.error('❌ Error:', error);
  }
}

async function queryMultiProjector() {
  console.log('\n🔍 Querying MultiProjectorActor...\n');

  const daprClient = new DaprClient({
    daprHost: "127.0.0.1",
    daprPort: "3513" // Multi-projector's Dapr port
  });

  try {
    const actorId = 'aggregatelistprojector-taskprojector';
    
    // Build state to ensure we have latest events
    console.log('📋 Building state...');
    const buildResponse = await daprClient.invoker.invoke(
      'dapr-sample-api-multi-projector',
      `actors/MultiProjectorActor/${actorId}/method/buildStateAsync`,
      HttpMethod.PUT,
      []
    );
    
    console.log('Build state response:', JSON.stringify(buildResponse, null, 2));
    
    // Query list
    console.log('\n📋 Querying task projections...');
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
    
    console.log('Query response:', JSON.stringify(queryResponse, null, 2));
    
    // Test queryAsync with specific filter
    const queryResult = queryResponse as { isSuccess: boolean; items?: Array<{ id: string }> };
    if (queryResult.isSuccess && queryResult.items?.length > 0) {
      const firstTaskId = queryResult.items[0].id;
      console.log(`\n📋 Querying specific task: ${firstTaskId}`);
      
      const singleQueryResponse = await daprClient.invoker.invoke(
        'dapr-sample-api-multi-projector',
        `actors/MultiProjectorActor/${actorId}/method/queryAsync`,
        HttpMethod.PUT,
        [{
          queryType: 'TaskProjector',
          payload: { id: firstTaskId }
        }]
      );
      
      console.log('Single query response:', JSON.stringify(singleQueryResponse, null, 2));
    }
    
  } catch (error) {
    console.error('❌ Error querying MultiProjectorActor:', error);
  }
}

async function testStorageProviders() {
  console.log('🚀 Testing MultiProjectorActor with different storage providers\n');
  
  // Test 1: InMemory (default)
  console.log('==== TEST 1: InMemory Storage ====');
  console.log('Current multi-projector is using: InMemory (default)\n');
  
  await createTestEvent();
  
  console.log('\n==== TEST 2: PostgreSQL Storage ====');
  console.log('To test with PostgreSQL:');
  console.log('1. Stop the multi-projector service');
  console.log('2. Restart with: USE_POSTGRES=true PORT=3013 DAPR_HTTP_PORT=3513 dapr run ...');
  console.log('3. Run this test again\n');
  
  // Check current storage type
  const isPostgres = process.env.USE_POSTGRES === 'true';
  console.log(`Current storage type: ${isPostgres ? 'PostgreSQL' : 'InMemory'}`);
}

// Run the test
testStorageProviders().catch(console.error);