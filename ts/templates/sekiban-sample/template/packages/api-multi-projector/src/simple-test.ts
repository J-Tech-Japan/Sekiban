import { DaprClient, HttpMethod } from '@dapr/dapr';

async function simpleTest() {
  console.log('🧪 Testing MultiProjectorActor directly...\n');

  const daprClient = new DaprClient({
    daprHost: "127.0.0.1",
    daprPort: "3513"  // Multi-projector's Dapr port
  });

  try {
    const actorId = 'aggregatelistprojector-taskprojector';
    
    // Test 1: Build state
    console.log('📋 Test 1: Building state on MultiProjectorActor');
    const buildResponse = await daprClient.invoker.invoke(
      'dapr-sample-api-multi-projector',
      `actors/MultiProjectorActor/${actorId}/method/buildStateAsync`,
      HttpMethod.PUT,
      []
    );
    
    console.log('✅ Build state response:', JSON.stringify(buildResponse, null, 2));
    
    // Test 2: Query list
    console.log('\n📋 Test 2: Querying list of projections');
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
    
    console.log('✅ Query response:', JSON.stringify(queryResponse, null, 2));
    
    // Test 3: Single query
    console.log('\n📋 Test 3: Single item query');
    const singleQueryResponse = await daprClient.invoker.invoke(
      'dapr-sample-api-multi-projector',
      `actors/MultiProjectorActor/${actorId}/method/queryAsync`,
      HttpMethod.PUT,
      [{
        queryType: 'TaskProjector',
        payload: { id: 'test-id' }
      }]
    );
    
    console.log('✅ Single query response:', JSON.stringify(singleQueryResponse, null, 2));
    
  } catch (error) {
    console.error('❌ Error:', error);
  }
}

// Run the test
simpleTest().catch(console.error);