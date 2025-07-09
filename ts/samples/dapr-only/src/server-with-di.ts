import express from 'express';
import { DaprServer, DaprClient, CommunicationProtocolEnum, HttpMethod } from '@dapr/dapr';
import { CounterActorWithDI } from './counter-actor-with-di.js';
import { initializeContainer } from './di-container.js';

async function start() {
  // Initialize the DI container first
  initializeContainer();
  console.log('✅ DI Container initialized');

  const app = express();
  app.use(express.json());

  // Create DaprClient to invoke actors from API endpoints
  const daprClient = new DaprClient({
    daprHost: "127.0.0.1",
    daprPort: process.env.DAPR_HTTP_PORT || "3504",
    communicationProtocol: CommunicationProtocolEnum.HTTP,
  });

  // Add a simple health check endpoint
  app.get('/health', (req, res) => {
    res.json({ 
      status: 'ok', 
      timestamp: new Date().toISOString(),
      di: 'enabled with Awilix'
    });
  });

  // Test DI endpoint
  app.get('/api/counter/:id/test-di', async (req, res) => {
    try {
      const actorId = req.params.id;
      console.log(`[API] Testing DI for actor ${actorId}`);
      
      const result = await daprClient.invoker.invoke(
        process.env.DAPR_APP_ID || 'counter-di-app',
        `actors/CounterActorWithDI/${actorId}/method/testDI`,
        HttpMethod.PUT,
        {}
      );
      
      res.json(result);
    } catch (error: any) {
      console.error('[API] Error testing DI:', error);
      res.status(500).json({ error: 'Failed to test DI', details: error.message });
    }
  });

  // Get counter value
  app.get('/api/counter/:id', async (req, res) => {
    try {
      const actorId = req.params.id;
      console.log(`[API] Getting counter value for actor ${actorId}`);
      
      const result = await daprClient.invoker.invoke(
        process.env.DAPR_APP_ID || 'counter-di-app',
        `actors/CounterActorWithDI/${actorId}/method/getCount`,
        HttpMethod.PUT,
        {}
      );
      
      res.json({ 
        actorId,
        count: result,
        di: 'enabled'
      });
    } catch (error: any) {
      console.error('[API] Error getting counter:', error);
      res.status(500).json({ error: 'Failed to get counter value', details: error.message });
    }
  });

  // Increment counter
  app.post('/api/counter/:id/increment', async (req, res) => {
    try {
      const actorId = req.params.id;
      console.log(`[API] Incrementing counter for actor ${actorId}`);
      
      // Call actor directly via HTTP
      const daprPort = process.env.DAPR_HTTP_PORT || '3504';
      const response = await fetch(
        `http://localhost:${daprPort}/v1.0/actors/CounterActorWithDI/${actorId}/method/increment`,
        {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({})
        }
      );
      
      if (!response.ok) {
        throw new Error(`Actor call failed: ${response.status}`);
      }
      
      const result = await response.json();
      console.log(`[API] Direct actor call returned:`, result);
      res.json({ 
        actorId,
        newCount: result,
        di: 'enabled'
      });
    } catch (error: any) {
      console.error('[API] Error incrementing counter:', error);
      res.status(500).json({ error: 'Failed to increment counter', details: error.message });
    }
  });

  // Decrement counter
  app.post('/api/counter/:id/decrement', async (req, res) => {
    try {
      const actorId = req.params.id;
      console.log(`[API] Decrementing counter for actor ${actorId}`);
      
      const result = await daprClient.invoker.invoke(
        process.env.DAPR_APP_ID || 'counter-di-app',
        `actors/CounterActorWithDI/${actorId}/method/decrement`,
        HttpMethod.PUT,
        {}
      );
      
      res.json({ 
        actorId,
        newCount: result,
        di: 'enabled'
      });
    } catch (error: any) {
      console.error('[API] Error decrementing counter:', error);
      res.status(500).json({ error: 'Failed to decrement counter', details: error.message });
    }
  });

  // Reset counter
  app.post('/api/counter/:id/reset', async (req, res) => {
    try {
      const actorId = req.params.id;
      console.log(`[API] Resetting counter for actor ${actorId}`);
      
      await daprClient.invoker.invoke(
        process.env.DAPR_APP_ID || 'counter-di-app',
        `actors/CounterActorWithDI/${actorId}/method/reset`,
        HttpMethod.PUT,
        {}
      );
      
      res.json({ 
        actorId,
        message: 'Counter reset',
        di: 'enabled'
      });
    } catch (error: any) {
      console.error('[API] Error resetting counter:', error);
      res.status(500).json({ error: 'Failed to reset counter', details: error.message });
    }
  });

  // Debug middleware to log all requests
  app.use((req, res, next) => {
    console.log(`[${new Date().toISOString()}] ${req.method} ${req.path}`);
    next();
  });

  // Create DaprServer with Express app
  const daprServer = new DaprServer({
    serverHost: "127.0.0.1",
    serverPort: process.env.PORT || "3004",
    serverHttp: app,
    communicationProtocol: CommunicationProtocolEnum.HTTP,
    clientOptions: {
      daprHost: "127.0.0.1",
      daprPort: process.env.DAPR_HTTP_PORT || "3504",
      communicationProtocol: CommunicationProtocolEnum.HTTP,
    }
  });

  // Initialize actors FIRST
  await daprServer.actor.init();
  console.log('✅ Actor system initialized');

  // Register the DI-enabled counter actor
  daprServer.actor.registerActor(CounterActorWithDI);
  console.log('✅ Registered CounterActorWithDI');

  // Start the server
  await daprServer.start();
  
  console.log(`
🚀 Dapr Counter Actor with Awilix DI Started!
📡 Server: http://localhost:${process.env.PORT || '3004'}
🎭 Actor: CounterActorWithDI
💉 DI: Enabled with Awilix

📋 API Endpoints:
   - GET  /api/counter/:id           - Get counter value
   - POST /api/counter/:id/increment - Increment counter
   - POST /api/counter/:id/decrement - Decrement counter
   - POST /api/counter/:id/reset     - Reset counter
   - GET  /api/counter/:id/test-di   - Test DI integration

📋 Other Endpoints:
   - GET  /health                    - Health check
   
📋 Actor Endpoints (direct):
   - PUT  /actors/CounterActorWithDI/:id/method/:method
  `);
}

start().catch((error) => {
  console.error('❌ Failed to start server:', error);
  process.exit(1);
});