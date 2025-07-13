import express from 'express';
import { DaprServer, DaprClient, CommunicationProtocolEnum, ActorProxyBuilder, ActorId } from '@dapr/dapr';
import { CounterActorWithDI } from './counter-actor-with-di.js';
import { initializeContainer } from './di-container.js';

// Define the interface for our actor
interface CounterActorInterface {
  increment(): Promise<number>;
  decrement(): Promise<number>;
  getCount(): Promise<number>;
  reset(): Promise<void>;
  testDI(): Promise<object>;
}

async function start() {
  // Initialize the DI container first
  initializeContainer();
  console.log('✅ DI Container initialized');

  const app = express();
  app.use(express.json());

  // Create DaprClient for ActorProxyBuilder
  const daprClient = new DaprClient({
    daprHost: "127.0.0.1",
    daprPort: process.env.DAPR_HTTP_PORT || "3504",
    communicationProtocol: CommunicationProtocolEnum.HTTP,
  });

  // Create ActorProxyBuilder
  const actorProxyBuilder = new ActorProxyBuilder<CounterActorInterface>(CounterActorWithDI, daprClient);

  // Add a simple health check endpoint
  app.get('/health', (req, res) => {
    res.json({ 
      status: 'ok', 
      timestamp: new Date().toISOString(),
      di: 'enabled with Awilix',
      method: 'ActorProxyBuilder'
    });
  });

  // Test DI endpoint
  app.get('/api/counter/:id/test-di', async (req, res) => {
    try {
      const actorId = new ActorId(req.params.id);
      console.log(`[API] Testing DI for actor ${actorId.getId()}`);
      
      const actor = actorProxyBuilder.build(actorId);
      const result = await actor.testDI();
      
      res.json(result);
    } catch (error: any) {
      console.error('[API] Error testing DI:', error);
      res.status(500).json({ error: 'Failed to test DI', details: error.message });
    }
  });

  // Get counter value
  app.get('/api/counter/:id', async (req, res) => {
    try {
      const actorId = new ActorId(req.params.id);
      console.log(`[API] Getting counter value for actor ${actorId.getId()}`);
      
      const actor = actorProxyBuilder.build(actorId);
      const count = await actor.getCount();
      
      res.json({ 
        actorId: actorId.getId(),
        count,
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
      const actorId = new ActorId(req.params.id);
      console.log(`[API] Incrementing counter for actor ${actorId.getId()}`);
      
      const actor = actorProxyBuilder.build(actorId);
      const newCount = await actor.increment();
      
      console.log(`[API] Actor proxy returned count: ${newCount}`);
      res.json({ 
        actorId: actorId.getId(),
        newCount,
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
      const actorId = new ActorId(req.params.id);
      console.log(`[API] Decrementing counter for actor ${actorId.getId()}`);
      
      const actor = actorProxyBuilder.build(actorId);
      const newCount = await actor.decrement();
      
      res.json({ 
        actorId: actorId.getId(),
        newCount,
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
      const actorId = new ActorId(req.params.id);
      console.log(`[API] Resetting counter for actor ${actorId.getId()}`);
      
      const actor = actorProxyBuilder.build(actorId);
      await actor.reset();
      
      res.json({ 
        actorId: actorId.getId(),
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
🔧 Method: ActorProxyBuilder

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