import { DaprClient, HttpMethod } from '@dapr/dapr';

async function testMultiProjectorActor() {
  console.log('🧪 Testing MultiProjectorActor...\n');

  const daprClient = new DaprClient({
    daprHost: "127.0.0.1",
    daprPort: "3503"
  });

  try {
    // Test 1: Call buildStateAsync on a MultiProjectorActor
    console.log('📋 Test 1: Calling buildStateAsync on MultiProjectorActor');
    const actorId = 'aggregatelistprojector-userprojector';
    
    const response = await daprClient.invoker.invoke(
      'dapr-sample-api-multi-projector',
      `actors/MultiProjectorActor/${actorId}/method/buildStateAsync`,
      HttpMethod.PUT,
      [] // Empty parameters array
    );
    
    console.log('✅ Response:', JSON.stringify(response, null, 2));
    
    // Test 2: Try queryAsync
    console.log('\n📋 Test 2: Calling queryAsync on MultiProjectorActor');
    const query = {
      projectorType: 'UserProjector',
      filter: {},
      skip: 0,
      take: 10
    };
    
    const response2 = await daprClient.invoker.invoke(
      'dapr-sample-api-multi-projector',
      `actors/MultiProjectorActor/${actorId}/method/queryAsync`,
      HttpMethod.PUT,
      [query]
    );
    
    console.log('✅ Response:', JSON.stringify(response2, null, 2));
    
    // Test 3: Use the REST API endpoint
    console.log('\n📋 Test 3: Testing REST API endpoint');
    const apiResponse = await fetch('http://localhost:3003/api/v1/multi-projections/UserProjector/test-id');
    const apiData = await apiResponse.json();
    console.log('✅ API Response:', JSON.stringify(apiData, null, 2));
    
  } catch (error) {
    console.error('❌ Error:', error);
  }
}

// Run the test
testMultiProjectorActor().catch(console.error);