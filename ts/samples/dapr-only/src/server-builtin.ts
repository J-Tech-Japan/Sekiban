import { DaprServer, CommunicationProtocolEnum } from '@dapr/dapr';
import { CounterActor } from './counter-actor.js';

async function start() {
  // Create DaprServer WITHOUT passing an Express app
  // This lets DaprServer create and manage its own Express instance
  const daprServer = new DaprServer({
    serverHost: "127.0.0.1",
    serverPort: "3000",
    communicationProtocol: CommunicationProtocolEnum.HTTP,
    clientOptions: {
      daprHost: "127.0.0.1",
      daprPort: "3500",
      communicationProtocol: CommunicationProtocolEnum.HTTP,
    }
  });

  // Initialize actors FIRST (this creates the routes)
  await daprServer.actor.init();
  console.log('✅ Actor system initialized');

  // THEN register the counter actor
  daprServer.actor.registerActor(CounterActor);
  console.log('✅ Registered CounterActor');

  // Start the server
  await daprServer.start();
  
  console.log(`
🚀 Dapr Counter Actor Sample Started!
📡 Server: http://localhost:3000
🎭 Actor: CounterActor
📋 Note: Using DaprServer's built-in Express server
  `);
}

start().catch((error) => {
  console.error('❌ Failed to start server:', error);
  process.exit(1);
});