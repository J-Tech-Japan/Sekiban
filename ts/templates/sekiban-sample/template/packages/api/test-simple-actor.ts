import express from 'express';
import { DaprServer, CommunicationProtocolEnum, AbstractActor } from '@dapr/dapr';

// Simple test actor
class TestActor extends AbstractActor {
  async testMethod(data: any): Promise<any> {
    console.log('TestActor.testMethod called with:', data);
    return { success: true, echo: data };
  }

  async onActivate(): Promise<void> {
    console.log('TestActor activated');
  }
}

async function startTestServer() {
  const app = express();
  app.use(express.json());

  // Add a test endpoint
  app.get('/test', (req, res) => {
    res.json({ message: 'Test server is running' });
  });

  // Debug middleware to log all requests
  app.use((req, res, next) => {
    console.log(`[${new Date().toISOString()}] ${req.method} ${req.path}`);
    next();
  });

  console.log('Creating DaprServer...');
  const daprServer = new DaprServer({
    serverHost: "127.0.0.1",
    serverPort: "3001", // Different port for testing
    serverHttp: app,
    communicationProtocol: CommunicationProtocolEnum.HTTP,
    clientOptions: {
      daprHost: "127.0.0.1",
      daprPort: "3500",
      communicationProtocol: CommunicationProtocolEnum.HTTP,
    }
  });

  console.log('Initializing actor runtime...');
  await daprServer.actor.init();

  console.log('Registering TestActor...');
  daprServer.actor.registerActor(TestActor);

  console.log('Starting DaprServer...');
  await daprServer.start();

  console.log('Test server is running on port 3001');
  console.log('Try: curl http://localhost:3001/test');
  console.log('Try: curl http://localhost:3001/dapr/config');
}

startTestServer().catch(console.error);