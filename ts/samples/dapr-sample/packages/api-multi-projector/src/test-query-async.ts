import { DaprClient, HttpMethod } from '@dapr/dapr';

async function testQueryAsync() {
  console.log('🧪 Testing MultiProjectorActor queryAsync method...\n');

  const daprClient = new DaprClient({
    daprHost: "127.0.0.1",
    daprPort: "3513"
  });

  try {
    const actorId = 'aggregatelistprojector-userprojector';
    
    // Test 1: Basic queryAsync
    console.log('📋 Test 1: Basic queryAsync');
    const query1 = {
      queryType: 'UserProjector',
      payload: {}
    };
    
    const response1 = await daprClient.invoker.invoke(
      'dapr-sample-api-multi-projector',
      `actors/MultiProjectorActor/${actorId}/method/queryAsync`,
      HttpMethod.PUT,
      [query1]
    );
    
    console.log('✅ Response:', JSON.stringify(response1, null, 2));
    
    // Test 2: queryAsync with filter
    console.log('\n📋 Test 2: queryAsync with id filter');
    const query2 = {
      queryType: 'UserProjector',
      payload: { id: 'test-user-id' }
    };
    
    const response2 = await daprClient.invoker.invoke(
      'dapr-sample-api-multi-projector',
      `actors/MultiProjectorActor/${actorId}/method/queryAsync`,
      HttpMethod.PUT,
      [query2]
    );
    
    console.log('✅ Response:', JSON.stringify(response2, null, 2));
    
    // Test 3: queryListAsync
    console.log('\n📋 Test 3: queryListAsync with pagination');
    const query3 = {
      queryType: 'UserProjector',
      payload: {},
      page: 1,
      pageSize: 10
    };
    
    const response3 = await daprClient.invoker.invoke(
      'dapr-sample-api-multi-projector',
      `actors/MultiProjectorActor/${actorId}/method/queryListAsync`,
      HttpMethod.PUT,
      [query3]
    );
    
    console.log('✅ Response:', JSON.stringify(response3, null, 2));
    
    console.log('\n✅ All tests completed successfully!');
    
  } catch (error) {
    console.error('❌ Error:', error);
  }
}

// Run the test
testQueryAsync().catch(console.error);