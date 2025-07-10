import express from 'express';
import { DaprServer, CommunicationProtocolEnum } from '@dapr/dapr';

/**
 * Debug tool to check what actors are being registered
 */
export async function debugDaprConfig() {
  const app = express();
  
  // Override the dapr/config endpoint to include both actors
  app.get('/dapr/config', (req, res) => {
    console.log('[DEBUG] Dapr config endpoint called');
    const config = {
      entities: ['AggregateActor', 'AggregateEventHandlerActor'],
      actorIdleTimeout: '1h',
      drainOngoingCallTimeout: '30s',
      drainRebalancedActors: true
    };
    console.log('[DEBUG] Returning config:', config);
    res.json(config);
  });
  
  const daprServer = new DaprServer({
    serverHost: "127.0.0.1",
    serverPort: "3009",
    serverHttp: app,
    communicationProtocol: CommunicationProtocolEnum.HTTP,
    clientOptions: {
      daprHost: "127.0.0.1",
      daprPort: "3509",
      communicationProtocol: CommunicationProtocolEnum.HTTP
    }
  });
  
  await daprServer.actor.init();
  
  // Try to get the registered actors
  console.log('[DEBUG] Actor runtime:', daprServer.actor);
  console.log('[DEBUG] Registered actors:', (daprServer.actor as any).registeredActors);
  
  await daprServer.start();
  console.log('[DEBUG] Server started');
}

debugDaprConfig().catch(console.error);