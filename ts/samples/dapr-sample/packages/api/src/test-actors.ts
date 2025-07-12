async function testDummyActor() {
  console.log('=== Testing DummyActor via HTTP ===');
  
  const actorType = 'DummyActor';
  const actorId = 'test-dummy-1';
  const method = 'testMethod';
  const daprPort = '3501';
  
  const url = `http://localhost:${daprPort}/v1.0/actors/${actorType}/${actorId}/method/${method}`;
  
  try {
    console.log(`Calling ${url}...`);
    const response = await fetch(url, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify('Hello from test script')
    });
    
    if (!response.ok) {
      const errorText = await response.text();
      throw new Error(`HTTP ${response.status}: ${errorText}`);
    }
    
    const result = await response.json();
    console.log('Success! Result:', result);
  } catch (error) {
    console.error('Failed to call DummyActor:', error);
  }
}

testDummyActor().catch(console.error);