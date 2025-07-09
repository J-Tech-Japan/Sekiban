import { ActorId, DaprClient, ActorProxyBuilder } from '@dapr/dapr';
import { CounterActorWithDI } from './counter-actor-with-di.js';

// Define the interface for our actor
interface CounterActorInterface {
  increment(): Promise<number>;
  decrement(): Promise<number>;
  getCount(): Promise<number>;
  reset(): Promise<void>;
  testDI(): Promise<object>;
}

async function testActorProxy() {
  const daprHost = "127.0.0.1";
  const daprPort = process.env.DAPR_HTTP_PORT || "3504";

  console.log(`Creating DaprClient with host: ${daprHost}, port: ${daprPort}`);
  const client = new DaprClient({ 
    daprHost, 
    daprPort,
    communicationProtocol: 'http'
  });

  // Create actor proxy builder
  const builder = new ActorProxyBuilder<CounterActorInterface>(CounterActorWithDI, client);

  // Create actor proxy
  const actorId = new ActorId("test-proxy-" + Date.now());
  const actor = builder.build(actorId);

  console.log(`Created actor proxy for ID: ${actorId.getId()}`);

  try {
    // Test increment
    console.log('\n=== Testing increment ===');
    const count1 = await actor.increment();
    console.log(`Count after increment: ${count1}`);

    // Test increment again
    const count2 = await actor.increment();
    console.log(`Count after second increment: ${count2}`);

    // Test getCount
    console.log('\n=== Testing getCount ===');
    const currentCount = await actor.getCount();
    console.log(`Current count: ${currentCount}`);

    // Test decrement
    console.log('\n=== Testing decrement ===');
    const count3 = await actor.decrement();
    console.log(`Count after decrement: ${count3}`);

    // Test DI
    console.log('\n=== Testing DI ===');
    const diResult = await actor.testDI();
    console.log('DI test result:', diResult);

    // Test reset
    console.log('\n=== Testing reset ===');
    await actor.reset();
    const finalCount = await actor.getCount();
    console.log(`Count after reset: ${finalCount}`);

    console.log('\n✅ All tests passed!');
  } catch (error) {
    console.error('❌ Test failed:', error);
  }
}

// Run the test
testActorProxy().catch(console.error);