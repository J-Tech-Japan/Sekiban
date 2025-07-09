import express from 'express';
import { DaprServer, DaprClient, CommunicationProtocolEnum, HttpMethod } from '@dapr/dapr';
import { CounterActor } from './counter-actor.js';

async function start() {
  const app = express();
  app.use(express.json());

  // Create DaprClient to invoke actors from API endpoints
  const daprClient = new DaprClient({
    daprHost: "127.0.0.1",
    daprPort: "3500",
    communicationProtocol: CommunicationProtocolEnum.HTTP,
  });

  // Add a simple health check endpoint
  app.get('/health', (req, res) => {
    res.json({ status: 'ok', timestamp: new Date().toISOString() });
  });

  // API endpoints that use DaprClient to invoke actors
  
  // Get counter value
  app.get('/api/counter/:id', async (req, res) => {
    try {
      const actorId = req.params.id;
      console.log(`[API] Getting counter value for actor ${actorId}`);
      
      // Use DaprClient invoker to call actor method
      const result = await daprClient.invoker.invoke(
        'counter-app',
        `actors/CounterActor/${actorId}/method/getCount`,
        HttpMethod.PUT,
        {}
      );
      
      res.json({ 
        actorId,
        count: result,
        via: 'DaprClient.invoker'
      });
    } catch (error: any) {
      console.error('[API] Error getting counter:', error);
      console.error('[API] Error details:', error.message, error.stack);
      res.status(500).json({ error: 'Failed to get counter value', details: error.message });
    }
  });

  // Increment counter
  app.post('/api/counter/:id/increment', async (req, res) => {
    try {
      const actorId = req.params.id;
      console.log(`[API] Incrementing counter for actor ${actorId}`);
      
      // Use DaprClient invoker to call actor method
      const result = await daprClient.invoker.invoke(
        'counter-app',
        `actors/CounterActor/${actorId}/method/increment`,
        HttpMethod.PUT,
        {}
      );
      
      res.json({ 
        actorId,
        newCount: result,
        via: 'DaprClient.invoker'
      });
    } catch (error: any) {
      console.error('[API] Error incrementing counter:', error);
      res.status(500).json({ error: 'Failed to increment counter' });
    }
  });

  // Decrement counter
  app.post('/api/counter/:id/decrement', async (req, res) => {
    try {
      const actorId = req.params.id;
      console.log(`[API] Decrementing counter for actor ${actorId}`);
      
      // Use DaprClient invoker to call actor method
      const result = await daprClient.invoker.invoke(
        'counter-app',
        `actors/CounterActor/${actorId}/method/decrement`,
        HttpMethod.PUT,
        {}
      );
      
      res.json({ 
        actorId,
        newCount: result,
        via: 'DaprClient.invoker'
      });
    } catch (error: any) {
      console.error('[API] Error decrementing counter:', error);
      res.status(500).json({ error: 'Failed to decrement counter' });
    }
  });

  // Reset counter
  app.post('/api/counter/:id/reset', async (req, res) => {
    try {
      const actorId = req.params.id;
      console.log(`[API] Resetting counter for actor ${actorId}`);
      
      // Use DaprClient invoker to call actor method
      await daprClient.invoker.invoke(
        'counter-app',
        `actors/CounterActor/${actorId}/method/reset`,
        HttpMethod.PUT,
        {}
      );
      
      res.json({ 
        actorId,
        message: 'Counter reset to 0',
        via: 'DaprClient.invoker'
      });
    } catch (error: any) {
      console.error('[API] Error resetting counter:', error);
      res.status(500).json({ error: 'Failed to reset counter' });
    }
  });

  // Debug middleware to log all requests
  app.use((req, res, next) => {
    console.log(`[${new Date().toISOString()}] ${req.method} ${req.path}`);
    next();
  });

  // Middleware to handle POST requests to actor endpoints
  // Dapr's invoke API sends POST, but actors expect PUT
  app.use((req, res, next) => {
    if (req.method === 'POST' && req.path.startsWith('/actors/')) {
      console.log(`Converting POST to PUT for actor endpoint: ${req.path}`);
      req.method = 'PUT';
    }
    next();
  });

  // Create DaprServer with Express app
  const daprServer = new DaprServer({
    serverHost: "127.0.0.1",
    serverPort: "3000",
    serverHttp: app,
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

📋 API Endpoints (using DaprClient.invoker):
   - GET  /api/counter/:id           - Get counter value
   - POST /api/counter/:id/increment - Increment counter
   - POST /api/counter/:id/decrement - Decrement counter
   - POST /api/counter/:id/reset     - Reset counter

📋 Other Endpoints:
   - GET  /health                    - Health check
   
📋 Actor Endpoints (direct):
   - PUT  /actors/CounterActor/:id/method/:method
  `);
}

start().catch((error) => {
  console.error('❌ Failed to start server:', error);
  process.exit(1);
});