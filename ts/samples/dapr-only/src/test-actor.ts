/**
 * Test script to call the counter actor methods
 */

const DAPR_HTTP_PORT = process.env.DAPR_HTTP_PORT || '3500';
const APP_ID = 'counter-app';
const ACTOR_TYPE = 'CounterActor';
const ACTOR_ID = 'counter-1';

const baseUrl = `http://localhost:${DAPR_HTTP_PORT}/v1.0/actors/${ACTOR_TYPE}/${ACTOR_ID}`;

async function invokeActorMethod(method: string, data?: any) {
  const url = `${baseUrl}/method/${method}`;
  console.log(`\n🔹 Calling ${method} on ${ACTOR_TYPE}/${ACTOR_ID}`);
  console.log(`   URL: ${url}`);
  console.log(`   Method: POST`);
  console.log(`   Body:`, data || '{}');
  
  try {
    const response = await fetch(url, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(data || {}),
    });

    const text = await response.text();
    
    if (!response.ok) {
      throw new Error(`HTTP ${response.status}: ${text}`);
    }

    try {
      const result = JSON.parse(text);
      console.log(`✅ Result:`, result);
      return result;
    } catch (e) {
      console.log(`✅ Result (text):`, text);
      return text;
    }
  } catch (error) {
    console.error(`❌ Error calling ${method}:`, error);
    throw error;
  }
}

async function runTests() {
  console.log('🧪 Starting Counter Actor Tests...\n');

  try {
    // Get initial count
    console.log('📊 Getting initial count...');
    await invokeActorMethod('getCount');

    // Increment a few times
    console.log('\n📈 Incrementing counter 3 times...');
    await invokeActorMethod('increment');
    await invokeActorMethod('increment');
    await invokeActorMethod('increment');

    // Get current count
    console.log('\n📊 Getting current count...');
    await invokeActorMethod('getCount');

    // Decrement once
    console.log('\n📉 Decrementing counter...');
    await invokeActorMethod('decrement');

    // Get current count
    console.log('\n📊 Getting current count...');
    await invokeActorMethod('getCount');

    // Reset
    console.log('\n🔄 Resetting counter...');
    await invokeActorMethod('reset');

    // Get final count
    console.log('\n📊 Getting final count...');
    await invokeActorMethod('getCount');

    console.log('\n✅ All tests completed successfully!');
  } catch (error) {
    console.error('\n❌ Test failed:', error);
    process.exit(1);
  }
}

// Wait a bit for the server to be ready
setTimeout(runTests, 2000);